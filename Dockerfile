# syntax=docker/dockerfile:1
# Imagem única parametrizada: PROJETO (o .csproj a publicar) e DLL (o assembly de entrada).

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG PROJETO
WORKDIR /src
COPY Directory.Build.props Directory.Packages.props ./
COPY src/ src/
RUN --mount=type=cache,target=/root/.nuget/packages,sharing=locked \
    dotnet publish "${PROJETO}" -c Release -o /app /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
ARG DLL
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_HTTP_PORTS=8080 \
    DLL=${DLL}
EXPOSE 8080
# Usuário sem privilégios de root (já existe nas imagens oficiais do .NET).
USER $APP_UID
ENTRYPOINT ["sh", "-c", "exec dotnet \"$DLL\""]
