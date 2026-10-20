global using System.Collections.Concurrent;
global using System.Globalization;
global using System.IdentityModel.Tokens.Jwt;
global using System.Net;
global using System.Net.Http.Json;
global using System.Security.Claims;
global using System.Text;
global using System.Text.Json;

global using Microsoft.AspNetCore.Hosting;
global using Microsoft.AspNetCore.Mvc.Testing;
global using Microsoft.AspNetCore.TestHost;
global using Microsoft.EntityFrameworkCore;
global using Microsoft.Extensions.DependencyInjection;
global using Microsoft.Extensions.Hosting;
global using Microsoft.Extensions.Logging;
global using Microsoft.Extensions.Options;
global using Microsoft.IdentityModel.Tokens;

global using RabbitMQ.Client;
global using Testcontainers.PostgreSql;
global using Testcontainers.RabbitMq;

global using ConsolidationService.Infrastructure.Auth;
global using ConsolidationService.Infrastructure.Messaging;
global using ConsolidationService.Infrastructure.Persistence;
global using ConsolidationService.Infrastructure.Projection;
global using ConsolidationService.Infrastructure.RateLimiting;
global using ConsolidationService.IntegrationTests.Infrastructure;
