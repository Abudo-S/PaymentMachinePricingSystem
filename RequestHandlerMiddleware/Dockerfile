#To overcome dependencyLib directory error, just run dockerfile from partent directory through:
#docker build . -t imageTag -f .\projFolder\Dockerfile
#Sometimes you'll need to deleted cached builds from docker desktop

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
EXPOSE 9000
EXPOSE 9001

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY ../RequestHandlerMiddleware/ RequestHandlerMiddleware/
COPY ../LibDTO/ LibDTO/
COPY ../LibProto/ LibProto/

RUN dotnet restore "./LibDTO/LibDTO.csproj"
RUN dotnet restore "./LibProto/LibProto.csproj"
RUN dotnet restore "./RequestHandlerMiddleware/RequestHandlerMiddleware.csproj"

RUN dotnet build "./RequestHandlerMiddleware/RequestHandlerMiddleware.csproj" -c Debug -o /app/build

FROM build AS publish
RUN dotnet publish "./RequestHandlerMiddleware/RequestHandlerMiddleware.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "RequestHandlerMiddleware.dll"]