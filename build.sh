#!/bin/bash
echo "Dependencies:"
#clear nuget package cache using "dotnet nuget locals all --clear" and also delete packages from csproj file
echo "dotnet add package Microsoft.AspNetCore.Owin -v 3.1"
echo "dotnet add package Dgraph"
echo "dotnet add package Microsoft.AspNetCore.Authentication.JwtBearer -v 3.1"
echo "building..."
dotnet build
echo "build finished, run it with dotnet run"
#dotnet run $@
