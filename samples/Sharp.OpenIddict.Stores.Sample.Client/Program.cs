using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenIddict();

var app = builder.Build();

app.MapGet("/", () => "Sharp.OpenIddict.Stores.Sample.Client");

app.Run();
