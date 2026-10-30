global using System.Collections.Concurrent;
global using System.IdentityModel.Tokens.Jwt;
global using System.Net;
global using System.Net.Http.Json;
global using System.Security.Claims;
global using System.Text;
global using System.Text.Json;

global using Microsoft.AspNetCore.Hosting;
global using Microsoft.AspNetCore.Mvc.Testing;
global using Microsoft.EntityFrameworkCore;
global using Microsoft.Extensions.DependencyInjection;
global using Microsoft.Extensions.Logging;
global using Microsoft.Extensions.Options;
global using Microsoft.IdentityModel.Tokens;

global using Npgsql;
global using Testcontainers.PostgreSql;

global using CashFlowEntryService.Infrastructure.Auth;
global using CashFlowEntryService.Infrastructure.Outbox;
global using CashFlowEntryService.Infrastructure.Persistence;
global using CashFlowEntryService.Infrastructure.RateLimiting;
global using CashFlowEntryService.IntegrationTests.Infrastructure;
