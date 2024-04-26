FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY ../DumbPaymentMachine/ DumbPaymentMachine/
COPY ../LibProto/ LibProto/
COPY ../DumbPaymentMachine/appsettings.json .


RUN dotnet restore "./LibProto/LibProto.csproj"
RUN dotnet restore "./DumbPaymentMachine/DumbPaymentMachine.csproj"

RUN dotnet build "./DumbPaymentMachine/DumbPaymentMachine.csproj" -c Debug -o /app/build

FROM build AS publish
RUN dotnet publish "./DumbPaymentMachine/DumbPaymentMachine.csproj" -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "DumbPaymentMachine.dll"]