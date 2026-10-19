using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using OpenIddict.Abstractions;
using Sharp.OpenIddict.Stores.Models;
using Xunit;

namespace Sharp.OpenIddict.Stores.Tests;

/// <summary>
/// Model contract from design 15.2/14.1: the four entities cover the native accessors of 7.7.2 column by
/// column without introducing extra columns, navigation properties, or non-UTC dates. The models are storage
/// POCOs; conversion and validation are the Store's responsibility.
/// </summary>
public sealed class ModelContractTests
{
    private static readonly (Type Entity, Type Store, string[] Columns)[] Entities =
    [
        (typeof(OpenIddictStoreApplication), typeof(IOpenIddictApplicationStore<>), [
            "ApplicationType", "ClientId", "ClientSecret", "ClientType", "ConsentType", "DisplayName",
            "DisplayNames", "JsonWebKeySet", "Permissions", "PostLogoutRedirectUris", "Properties",
            "RedirectUris", "Requirements", "Settings"]),
        (typeof(OpenIddictStoreAuthorization), typeof(IOpenIddictAuthorizationStore<>), [
            "ApplicationId", "CreationDate", "Properties", "Scopes", "Status", "Subject", "Type"]),
        (typeof(OpenIddictStoreScope), typeof(IOpenIddictScopeStore<>), [
            "Description", "Descriptions", "DisplayName", "DisplayNames", "Name", "Properties", "Resources"]),
        (typeof(OpenIddictStoreToken), typeof(IOpenIddictTokenStore<>), [
            "ApplicationId", "AuthorizationId", "CreationDate", "ExpirationDate", "Payload", "Properties",
            "RedemptionDate", "ReferenceId", "Status", "Subject", "Type"]),
    ];

    [Fact]
    public void ModelsAssembly_ContainsOnlyTheFourEntities()
    {
        var assembly = typeof(OpenIddictStoreApplication).Assembly;

        var types = assembly.GetExportedTypes()
            .Select(type => type.Name)
            .OrderBy(name => name, StringComparer.Ordinal);

        Assert.Equal(
            ["OpenIddictStoreApplication", "OpenIddictStoreAuthorization", "OpenIddictStoreScope", "OpenIddictStoreToken"],
            types);
    }

    [Fact]
    public void Entities_HaveExactlyTheDocumentedColumns()
    {
        foreach (var (entity, _, columns) in Entities)
        {
            var expected = new[] { "Id", "ConcurrencyToken" }.Concat(columns).OrderBy(name => name, StringComparer.Ordinal);
            var actual = entity.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Select(property => property.Name)
                .OrderBy(name => name, StringComparer.Ordinal);

            Assert.Equal(expected, actual);
        }
    }

    [Fact]
    public void EveryNativeAccessor_HasAColumn()
    {
        var missing = new List<string>();

        foreach (var (entity, store, columns) in Entities)
        {
            var checkedAccessors = 0;

            foreach (var method in store.GetMethods())
            {
                if (!method.Name.StartsWith("Get", StringComparison.Ordinal)
                    && !method.Name.StartsWith("Set", StringComparison.Ordinal))
                {
                    continue;
                }

                var parameters = method.GetParameters();

                // The first parameter of a property accessor is the entity itself; the first parameter of a query callback is a Func<...>.
                if (parameters.Length == 0 || !parameters[0].ParameterType.IsGenericParameter)
                {
                    continue;
                }

                checkedAccessors++;

                var column = method.Name[3..^"Async".Length];
                if (entity.GetProperty(column) is null)
                {
                    missing.Add($"{entity.Name}.{column} <- {method.Name}");
                }
            }

            // Every business column has a Get/Set accessor pair, plus GetIdAsync; ConcurrencyToken is maintained only inside the Store.
            Assert.True(checkedAccessors == (2 * columns.Length) + 1,
                $"{entity.Name} has {checkedAccessors} accessors but should have {2 * columns.Length + 1}.");
        }

        Assert.True(missing.Count == 0,
            "Native accessors are missing a matching column:" + Environment.NewLine + string.Join(Environment.NewLine, missing));
    }

    [Fact]
    public void Entities_AreExtensiblePocosWithoutNavigationProperties()
    {
        var entityTypes = Entities.Select(entry => entry.Entity).ToHashSet();

        foreach (var (entity, _, _) in Entities)
        {
            Assert.True(entity.IsPublic && !entity.IsSealed, $"{entity.Name} must be inheritable.");

            foreach (var property in entity.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                Assert.True(property.GetMethod!.IsVirtual, $"{entity.Name}.{property.Name} must be virtual.");

                var type = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
                Assert.False(entityTypes.Contains(type),
                    $"{entity.Name}.{property.Name} must not be a navigation property.");
            }
        }
    }

    [Fact]
    public void DatesAreNullableUtcAndCollectionsAreJson()
    {
        foreach (var (entity, _, _) in Entities)
        {
            foreach (var property in entity.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                var type = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;

                if (type == typeof(DateTime))
                {
                    Assert.Equal(typeof(DateTime), Nullable.GetUnderlyingType(property.PropertyType));
                }

                Assert.True(
                    property.PropertyType == typeof(string)
                    || property.PropertyType == typeof(DateTime?),
                    $"{entity.Name}.{property.Name} must be either string or DateTime?, but is {property.PropertyType}.");
            }
        }
    }

    [Fact]
    public void IdentifiersAndConcurrencyTokens_AreFreshGuids()
    {
        foreach (var (entity, _, _) in Entities)
        {
            var first = Activator.CreateInstance(entity)!;
            var second = Activator.CreateInstance(entity)!;

            foreach (var name in new[] { "Id", "ConcurrencyToken" })
            {
                var property = entity.GetProperty(name)!;
                var value = (string?)property.GetValue(first);

                Assert.True(Guid.TryParse(value, out _), $"{entity.Name}.{name} must be a GUID string, but is {value}.");
                Assert.NotEqual(value, property.GetValue(second));
            }
        }
    }
}
