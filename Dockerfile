FROM mcr.microsoft.com/dotnet/sdk:7.0 as build

WORKDIR /app

COPY ./Realsearch.sln .
COPY ./API/API.csproj ./API/
COPY ./API.Contracts/API.Contracts.csproj ./API.Contracts/

RUN dotnet restore

COPY . .

# RUN dotnet publish --no-restore -c Release -o ./published ./API/API.csproj
RUN dotnet publish -c Release -o ./published ./API/API.csproj

FROM mcr.microsoft.com/dotnet/aspnet:7.0 as runtime

# Uncomment the line below if running with HTTPS
# ENV ASPNETCORE_URLS=https://+:443

WORKDIR /app

COPY --from=build /app/published .

ENTRYPOINT [ "dotnet", "API.dll" ]