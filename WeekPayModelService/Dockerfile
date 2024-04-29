#To overcome dependencyLib directory error, just run dockerfile from partent directory through:
#docker build . -t imageTag -f .\projFolder\Dockerfile
#Sometimes you'll need to deleted cached builds from docker desktop

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY ../WeekPayModelService/ WeekPayModelService/
COPY ../LibDTO/ LibDTO/
COPY ../LibProto/ LibProto/
COPY ../LibHelpers/ LibHelpers/
COPY ../WeekPayModelService/appsettings.json .


RUN dotnet restore "./LibHelpers/LibHelpers.csproj"
RUN dotnet restore "./LibDTO/LibDTO.csproj"
RUN dotnet restore "./LibProto/LibProto.csproj"
RUN dotnet restore "./WeekPayModelService/WeekPayModelService.csproj"

RUN dotnet build "./WeekPayModelService/WeekPayModelService.csproj" -c Debug -o /app/build

FROM build AS publish
RUN dotnet publish "./WeekPayModelService/WeekPayModelService.csproj" -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "WeekPayModelService.dll"]