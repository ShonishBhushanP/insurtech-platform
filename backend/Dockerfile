# Generic multi-stage build for any InsurTech .NET 8 service.
# Build context = backend/. Pass the project path and output DLL via build args.
#   az acr build --build-arg PROJECT=src/Services/Claims/Claims.Api/Claims.Api.csproj --build-arg DLL=Claims.Api.dll ...
ARG PROJECT
ARG DLL

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
ARG PROJECT
WORKDIR /src
COPY . .
RUN dotnet publish "$PROJECT" -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
ARG DLL
WORKDIR /app
COPY --from=build /app .
ENV APP_DLL=$DLL
# Listen on 8080 in-container; overrides the local dev Kestrel binding from appsettings.
ENV ASPNETCORE_URLS=http://+:8080
ENV Kestrel__Endpoints__Http__Url=http://+:8080
EXPOSE 8080
ENTRYPOINT ["sh", "-c", "exec dotnet $APP_DLL"]
