FROM node:20-alpine AS frontend
WORKDIR /build
COPY frontend/ ./frontend/
WORKDIR /build/frontend
RUN npm ci && npm run build

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /repo
COPY . .
COPY --from=frontend /build/src/Onix.Scanner.Api/wwwroot ./src/Onix.Scanner.Api/wwwroot
RUN dotnet publish src/Onix.Scanner.Api/Onix.Scanner.Api.csproj -c Release -o /publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /publish .
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080
ENTRYPOINT ["dotnet", "Onix.Scanner.Api.dll"]
