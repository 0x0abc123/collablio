#!/bin/bash
echo "Dependencies:"
echo "dotnet add package Microsoft.AspNetCore.Owin -v 3.1"
echo "dotnet add package Dgraph"
echo "building..."
dotnet build
echo "build finished, run it with dotnet run"
#dotnet run $@
