using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.Json;
using Dapper;
using JacRed.Api;
using JacRed.Api.Configuration;
using JacRed.Core.Models.Options;
using JacRed.Infrastructure.Migrations.Configurations;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Sinks.SystemConsole.Themes;

var safeLiterateTheme = new AnsiConsoleTheme(new Dictionary<ConsoleThemeStyle, string>
{
    [ConsoleThemeStyle.Text] = "\x1b[37m",           // Белый (обычный текст)
    [ConsoleThemeStyle.SecondaryText] = "\x1b[90m",  // Серый (скобки, детали)
    [ConsoleThemeStyle.TertiaryText] = "\x1b[90m",   // Серый
    [ConsoleThemeStyle.Invalid] = "\x1b[33m",        // Желтый
    [ConsoleThemeStyle.Null] = "\x1b[34m",           // Синий
    [ConsoleThemeStyle.Name] = "\x1b[37m",           // Белый (имена свойств)
    [ConsoleThemeStyle.String] = "\x1b[36m",         // Голубой (как в Literate!)
    [ConsoleThemeStyle.Number] = "\x1b[35m",         // Фиолетовый (цифры)
    [ConsoleThemeStyle.Boolean] = "\x1b[34m",        // Синий (true/false)
    [ConsoleThemeStyle.Scalar] = "\x1b[32m",         // Зеленый
    [ConsoleThemeStyle.LevelVerbose] = "\x1b[90m",   // Серый
    [ConsoleThemeStyle.LevelDebug] = "\x1b[90m",     // Серый
    [ConsoleThemeStyle.LevelInformation] = "\x1b[34;1m", // Ярко-синий (Инфо)
    [ConsoleThemeStyle.LevelWarning] = "\x1b[33;1m", // Ярко-желтый
    [ConsoleThemeStyle.LevelError] = "\x1b[31;1m",   // Ярко-красный
    [ConsoleThemeStyle.LevelFatal] = "\x1b[31;1m",   // Ярко-красный
});

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .Enrich.FromLogContext()
    .WriteTo.Console(
        theme: safeLiterateTheme,
        applyThemeToRedirectedOutput: true,
        outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
    .CreateLogger();

var builder = WebApplication.CreateBuilder(args);

builder.Logging.ClearProviders();
builder.Host.UseSerilog(Log.Logger, dispose: true);

// Dapper: сопоставление snake_case колонок с PascalCase свойствами
DefaultTypeMap.MatchNamesWithUnderscores = true;

// 1. Добавляем файл в общую конфигурацию приложения
builder.Configuration.AddYamlFile("config.local.yml", false, true);

// 2. Регистрируем IOptions (теперь builder.Configuration содержит данные из YAML)
builder.Services.Configure<Config>(builder.Configuration);

// 3. Настраиваем Kestrel
builder.WebHost.UseKestrel((context, kestrelOptions) =>
{
    var serverOpts = context.Configuration.Get<Config>() ?? new Config();

    var listenIp = serverOpts.ListenIp;
    var port = serverOpts.ListenPort;

    var ip = listenIp.Equals("any", StringComparison.OrdinalIgnoreCase)
        ? IPAddress.Any
        : IPAddress.Parse(listenIp);

    kestrelOptions.Listen(ip, port);
});

// --- Глобальные настройки ---
CultureInfo.CurrentCulture = new CultureInfo("ru-RU");
Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

// --- Сервисы ---
builder.Services.AddControllers();

builder.Services.Configure<ApiBehaviorOptions>(options =>
{
    options.InvalidModelStateResponseFactory = context =>
    {
        var errors = context.ModelState
            .Where(e => e.Value?.Errors.Count > 0)
            .SelectMany(e => e.Value!.Errors)
            .Select(e => e.ErrorMessage)
            .Distinct()
            .ToArray();

        return new BadRequestObjectResult(new
        {
            error = "Validation failed",
            details = errors
        });
    };
});

builder.Services.AddResponseCompression(options =>
{
    options.MimeTypes = ResponseCompressionDefaults.MimeTypes
        .Concat(["application/vnd.apple.mpegurl", "image/svg+xml"]);
});

builder.Services.AddRouting(options => options.LowercaseUrls = true);

// --- Регистрация PostgreSQL ---
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
                       ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");

// Регистрация строки подключения как именованной строки (если нужно)
builder.Services.AddSingleton(connectionString);

// --- Регистрация зависимостей ---
builder.Services.RegisterServices();
builder.Services.AddJacRedMigrations(connectionString);

var app = builder.Build();

// --- Middleware ---
if (app.Environment.IsDevelopment())
    app.UseDeveloperExceptionPage();
else
    app.UseExceptionHandler(errorApp =>
    {
        errorApp.Run(async context =>
        {
            context.Response.StatusCode = 500;
            context.Response.ContentType = "application/json";

            await context.Response.WriteAsync(new
            {
                error = "Internal server error",
                message = "An unexpected error occurred. Please try again later."
            }.ToJson());
        });
    });

app.UseForwardedHeaders(new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
});

app.UseRouting();
app.UseResponseCompression();

var options = app.Configuration.Get<Config>();
if (options.Web) app.UseStaticFiles();

app.UseModHeaders();
app.MapControllers();

// --- Миграция БД ---
app.Services.RunJacRedMigrations();

// --- Запуск приложения ---
await app.RunAsync();

// --- Вспомогательные методы ---
namespace JacRed.Api
{
    internal static class Extensions
    {
        public static string ToJson(this object obj)
        {
            return JsonSerializer.Serialize(obj, new JsonSerializerOptions
            {
                PropertyNamingPolicy = null
            });
        }
    }
}