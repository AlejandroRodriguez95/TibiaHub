namespace TibiaIntel.Routes;

public static class ApiRoutes
{
    public const string Account = "api/account";
    public const string Character = "api/character";

    public static class AccountRoutes
    {
        public const string Register = "register";
        public const string Login = "login";
        public const string MyAccount = "myaccount";
        public const string Logout = "logout";
        public const string Csrf = "csrf";
        public const string UserById = "users/{id}";

        public static string UserLocation(string id) => $"/{Account}/users/{id}";
    }

    public static class CharacterRoutes
    {
        public const string ByName = "{name}";
    }
}
