FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY TibiaIntel/TibiaIntel.csproj TibiaIntel/
RUN dotnet restore TibiaIntel/TibiaIntel.csproj
COPY TibiaIntel/ TibiaIntel/
RUN dotnet publish TibiaIntel/TibiaIntel.csproj -c Release --no-restore -o /out /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /out .
RUN mkdir -p /var/lib/tibiaintel/refresh /var/lib/tibiaintel/keys \
    && chown -R app:app /var/lib/tibiaintel \
    && chmod 700 /var/lib/tibiaintel/keys
USER app
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "TibiaIntel.dll"]
