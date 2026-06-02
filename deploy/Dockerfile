FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app

# Npgsql requires libgssapi-krb5-2 for Kerberos/GSSAPI support
RUN apt-get update && apt-get install -y \
    libgssapi-krb5-2 \
    && rm -rf /var/lib/apt/lists/*

COPY publish/ .
ENTRYPOINT ["dotnet", "Messenger.Api.dll"]
