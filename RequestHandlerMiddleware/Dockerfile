FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY ../RequestHandlerMiddleware/ RequestHandlerMiddleware/
COPY ../LibDTO/ LibDTO/
COPY ../LibProto/ LibProto/
COPY ../RequestHandlerMiddleware/appsettings.json .

RUN dotnet restore "./LibDTO/LibDTO.csproj"
RUN dotnet restore "./LibProto/LibProto.csproj"
RUN dotnet restore "./RequestHandlerMiddleware/RequestHandlerMiddleware.csproj"

RUN dotnet build "./RequestHandlerMiddleware/RequestHandlerMiddleware.csproj" -c Debug -o /app/build

FROM build AS publish
RUN dotnet publish "./RequestHandlerMiddleware/RequestHandlerMiddleware.csproj" -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "RequestHandlerMiddleware.dll"]