global using System.Diagnostics;
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
global using Microsoft.IdentityModel.Tokens;

global using RabbitMQ.Client;
global using Testcontainers.PostgreSql;
global using Testcontainers.RabbitMq;

global using LaunchService.Domain;
global using LaunchService.Infrastructure.Messaging;
global using LaunchService.Infrastructure.Outbox;
global using LaunchService.Infrastructure.Persistence;
global using LaunchService.OutboxPublisherTests.Infrastructure;
