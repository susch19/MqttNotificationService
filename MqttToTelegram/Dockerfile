
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS base
WORKDIR /app

ARG TARGETPLATFORM

FROM mcr.microsoft.com/dotnet/sdk:9.0-bookworm-slim-amd64 AS build
WORKDIR /src
COPY ["MqttToTelegram/MqttToTelegram.csproj", "MqttToTelegram/"]
RUN dotnet restore "MqttToTelegram/MqttToTelegram.csproj"
COPY . .
WORKDIR "/src/MqttToTelegram"

FROM build AS publish
ARG TARGETPLATFORM
RUN if [ "$TARGETPLATFORM" = "linux/amd64" ]; then \
        RID=linux-x64 ; \
    elif [ "$TARGETPLATFORM" = "linux/arm64" ]; then \
        RID=linux-arm64 ; \
    elif [ "$TARGETPLATFORM" = "linux/arm/v7" ]; then \
        RID=linux-arm ; \
    fi \
    && dotnet publish -c Release -r $RID -o /app/publish /p:UseAppHost=false --self-contained false 


FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "MqttToTelegram.dll"]