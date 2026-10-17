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
        using var client = await fixture.CreateAccountClientAsync();
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
        using var client = await fixture.CreateAccountClientAsync();
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
        using var client = await fixture.CreateAccountClientAsync();
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
        using var client = await fixture.CreateAccountClientAsync();
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

    [Fact]
    public async Task Valid_login_returns_user_and_authentication_cookie()
    {
        using var client = await fixture.CreateAccountClientAsync();
        using var registration = await client.PostAsJsonAsync("/account/register", new
        {
            Email = "player@example.com", Password = ValidPassword
        });
        Assert.Equal(HttpStatusCode.Created, registration.StatusCode);

        using var response = await client.PostAsJsonAsync("/account/login", new
        {
            Email = "PLAYER@example.com", Password = ValidPassword
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("player@example.com", body.GetProperty("email").GetString());
        Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("id").GetString()));
        Assert.False(body.TryGetProperty("passwordHash", out _));
        Assert.Contains(response.Headers.GetValues("Set-Cookie"),
            cookie => cookie.StartsWith(".AspNetCore.Identity.Application="));
    }

    [Theory]
    [InlineData("player@example.com", "WrongPassword123!")]
    [InlineData("unknown@example.com", ValidPassword)]
    public async Task Invalid_credentials_return_unauthorized_without_cookie(string email, string password)
    {
        using var client = await fixture.CreateAccountClientAsync();
        using var registration = await client.PostAsJsonAsync("/account/register", new
        {
            Email = "player@example.com", Password = ValidPassword
        });
        Assert.Equal(HttpStatusCode.Created, registration.StatusCode);

        using var response = await client.PostAsJsonAsync("/account/login", new { Email = email, Password = password });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.False(response.Headers.Contains("Set-Cookie"));
    }

    [Theory]
    [InlineData(null, ValidPassword)]
    [InlineData("not-an-email", ValidPassword)]
    [InlineData("player@example.com", "")]
    public async Task Invalid_login_request_returns_bad_request(string? email, string password)
    {
        using var client = await fixture.CreateAccountClientAsync();
        using var response = await client.PostAsJsonAsync("/account/login", new { Email = email, Password = password });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.False(response.Headers.Contains("Set-Cookie"));
    }

    private async Task AssertUserCountAsync(int expected)
    {
        using var scope = fixture.Services.CreateScope();
        Assert.Equal(expected, await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Users.CountAsync());
    }

    [Fact]
    public async Task Cookie_identifies_user_until_logout()
    {
        using var client = await fixture.CreateAccountClientAsync();
        using var anonymous = await client.GetAsync("/account/myaccount");
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        Assert.Null(anonymous.Headers.Location);
        var credentials = new { Email = "session@example.com", Password = ValidPassword };
        using var registration = await client.PostAsJsonAsync("/account/register", credentials);
        Assert.Equal(HttpStatusCode.Created, registration.StatusCode);
        using var afterRegistration = await client.GetAsync("/account/myaccount");
        Assert.Equal(HttpStatusCode.Unauthorized, afterRegistration.StatusCode);
        using var login = await client.PostAsJsonAsync("/account/login", credentials);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var expected = await login.Content.ReadFromJsonAsync<JsonElement>();
        var actual = await client.GetFromJsonAsync<JsonElement>("/account/myaccount");
        Assert.Equal(expected.GetProperty("id").GetString(), actual.GetProperty("id").GetString());
        Assert.Equal(credentials.Email, actual.GetProperty("email").GetString());
        using var otherClient = await fixture.CreateAccountClientAsync();
        using var isolated = await otherClient.GetAsync("/account/myaccount");
        Assert.Equal(HttpStatusCode.Unauthorized, isolated.StatusCode);
        await RegistrationFixture.RefreshCsrfAsync(client);
        using var logout = await client.PostAsync("/account/logout", null);
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        using var afterLogout = await client.GetAsync("/account/myaccount");
        Assert.Equal(HttpStatusCode.Unauthorized, afterLogout.StatusCode);
    }

    [Theory]
    [InlineData("register")]
    [InlineData("login")]
    public async Task Account_writes_require_valid_csrf_token(string action)
    {
        using var client = await fixture.CreateAccountClientAsync();
        client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        using var response = await client.PostAsJsonAsync($"/account/{action}", new
        {
            Email = "csrf@example.com", Password = ValidPassword
        });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await AssertUserCountAsync(0);
    }

    [Fact]
    public async Task Logout_rejects_invalid_csrf_without_ending_session()
    {
        using var client = await fixture.CreateAccountClientAsync();
        var credentials = new { Email = "csrf@example.com", Password = ValidPassword };
        using var registration = await client.PostAsJsonAsync("/account/register", credentials);
        Assert.Equal(HttpStatusCode.Created, registration.StatusCode);
        using var login = await client.PostAsJsonAsync("/account/login", credentials);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", "invalid");
        using var logout = await client.PostAsync("/account/logout", null);
        Assert.Equal(HttpStatusCode.BadRequest, logout.StatusCode);
        using var me = await client.GetAsync("/account/myaccount");
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
    }
}
