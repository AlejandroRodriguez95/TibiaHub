using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TibiaHub.Data;
using TibiaHub.Identity;

namespace TibiaHub.Tests;

public sealed class RegistrationTests(RegistrationFixture fixture) : IClassFixture<RegistrationFixture>, IAsyncLifetime
{
    private const string ValidPassword = "ValidPassword123!";

    public Task InitializeAsync() => fixture.ResetUsersAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Valid_registration_returns_created_and_stores_hashed_password()
    {
        using var client = fixture.CreateClient();
        using var response = await client.PostAsJsonAsync("/account/register", new
        {
            Email = "player@example.com", Password = ValidPassword
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var id = body.GetProperty("id").GetString();
        Assert.False(string.IsNullOrWhiteSpace(id));
        Assert.Equal("player@example.com", body.GetProperty("email").GetString());
        Assert.Equal($"/account/users/{id}", response.Headers.Location?.OriginalString);
        Assert.False(body.TryGetProperty("password", out _));
        Assert.False(body.TryGetProperty("passwordHash", out _));

        using var scope = fixture.Services.CreateScope();
        var user = await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Users.SingleAsync();
        Assert.Equal(id, user.Id);
        Assert.Equal("player@example.com", user.Email);
        Assert.Equal(user.Email, user.UserName);
        Assert.False(string.IsNullOrWhiteSpace(user.PasswordHash));
        Assert.NotEqual(ValidPassword, user.PasswordHash);
        Assert.True(await scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>()
            .CheckPasswordAsync(user, ValidPassword));
    }

    [Theory]
    [InlineData("player@example.com")]
    [InlineData("PLAYER@EXAMPLE.COM")]
    public async Task Duplicate_email_returns_bad_request_without_creating_another_user(string email)
    {
        using var client = fixture.CreateClient();
        using var first = await client.PostAsJsonAsync("/account/register", new
        {
            Email = "player@example.com", Password = ValidPassword
        });
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        using var response = await client.PostAsJsonAsync("/account/register", new { Email = email, Password = ValidPassword });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains(body.GetProperty("errors").EnumerateArray(),
            error => error.GetProperty("code").GetString() == "DuplicateEmail");
        await AssertUserCountAsync(1);
    }

    [Theory]
    [InlineData(null, ValidPassword)]
    [InlineData("", ValidPassword)]
    [InlineData("not-an-email", ValidPassword)]
    [InlineData("player@example.com", null)]
    [InlineData("player@example.com", "")]
    [InlineData("player@example.com", "Aa1!")]
    public async Task Invalid_request_returns_validation_errors_without_creating_user(string? email, string? password)
    {
        using var client = fixture.CreateClient();
        using var response = await client.PostAsJsonAsync("/account/register", new { Email = email, Password = password });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var field = string.IsNullOrEmpty(email) || email == "not-an-email" ? "Email" : "Password";
        Assert.True(body.GetProperty("errors").GetProperty(field).GetArrayLength() > 0);
        await AssertUserCountAsync(0);
    }

    [Theory]
    [InlineData("password123!", "PasswordRequiresUpper")]
    [InlineData("PASSWORD123!", "PasswordRequiresLower")]
    [InlineData("Password!!!", "PasswordRequiresDigit")]
    [InlineData("Password123", "PasswordRequiresNonAlphanumeric")]
    public async Task Weak_password_returns_identity_error_without_creating_user(string password, string expectedCode)
    {
        using var client = fixture.CreateClient();
        using var response = await client.PostAsJsonAsync("/account/register", new
        {
            Email = "player@example.com", Password = password
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains(body.GetProperty("errors").EnumerateArray(),
            error => error.GetProperty("code").GetString() == expectedCode);
        await AssertUserCountAsync(0);
    }

    private async Task AssertUserCountAsync(int expected)
    {
        using var scope = fixture.Services.CreateScope();
        Assert.Equal(expected, await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Users.CountAsync());
    }
}
