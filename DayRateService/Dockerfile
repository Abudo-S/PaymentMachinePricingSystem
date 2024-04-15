#See https://aka.ms/customizecontainer to learn how to customize your debug container and how Visual Studio uses this Dockerfile to build your images for faster debugging.

#Depending on the operating system of the host machines(s) that will build or run the containers, the image specified in the FROM statement may need to be changed.
#For more information, please see https://aka.ms/containercompat

#FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build-env
#WORKDIR /app
#
## Copy csproj and restore as distinct layers
##COPY *.csproj ./
#COPY . .
#RUN dotnet restore
#
## Copy everything else and build
##COPY . ./
#RUN dotnet publish -o /app
#
## Build runtime image
#FROM mcr.microsoft.com/dotnet/aspnet:8.0
#WORKDIR /app
#COPY --from=build-env /app .
#
#EXPOSE 5000
#EXPOSE 5001
#ENV ASPNETCORE_URLS=http://+:5000
#ENV ASPNETCORE_URLS=https://+:5001
#
## Command used to start the project
#ENTRYPOINT ["dotnet", "DayRateService.dll"]

#To overcome dependencyLib directory error, just run dockerfile from partent directory through:
#docker build . -t imageTag -f .\projFolder\Dockerfile
#Sometimes you'll need to deleted cached builds from docker desktop
#To add relative redis image with container: docker run --name dayrateservice_redis -p 5002:6379 -d redis
#To run existing  .net image with configuration: docker run --name dayrateservice --rm -it -p 8000:80 -p 8001:443 -e ASPNETCORE_URLS="https://+;http://+" -e ASPNETCORE_HTTPS_PORTS=8001  -e ASPNETCORE_HTTP_PORTS=8000 -d dayrateservice
#only http container: docker run --name dayrateservice --rm -it -p 8000:80 -e ASPNETCORE_URLS="http://+" -e ASPNETCORE_HTTP_PORTS=80 -d dayrateservice
#create network bridge and add running containers to them: docker network create paymentMachinePricingSystem_network
#docker network connect paymentMachinePricingSystem_network dayrateservice
#docker network connect paymentMachinePricingSystem_network dayrateservice_redis
#inspect service container ip: docker inspect --format='{{.NetworkSettings.IPAddress}}' dayrateservice_redis

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
EXPOSE 8080
EXPOSE 8081

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY ../DayRateService/ DayRateService/
COPY ../LibDTO/ LibDTO/
COPY ../LibProto/ LibProto/
COPY ../LibHelpers/ LibHelpers/


RUN dotnet restore "./LibHelpers/LibHelpers.csproj"
RUN dotnet restore "./LibDTO/LibDTO.csproj"
RUN dotnet restore "./LibProto/LibProto.csproj"
RUN dotnet restore "./DayRateService/DayRateService.csproj"

RUN dotnet build "./DayRateService/DayRateService.csproj" -c Debug -o /app/build

FROM build AS publish
RUN dotnet publish "./DayRateService/DayRateService.csproj" -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "DayRateService.dll"]

## Use a base image with the .NET 8 runtime
#FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
#
## Set working directory within the container
#WORKDIR /app
#
## Copy project files from current directory
#COPY . .
#
#FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
## Restore dependencies using NuGet
#RUN dotnet restore
#
## Publish the application (production-ready)
#RUN dotnet publish -c Release -o out/publish
#
## Final image stage (copies published artifacts)
#FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
#
## Copy published artifacts from build stage
#COPY --from=build /app/out/publish .
#
## Expose port 80 for web traffic
#EXPOSE 80
#
## Set the entry point to run the application
#ENTRYPOINT ["dotnet", "DayRateService.dll"]