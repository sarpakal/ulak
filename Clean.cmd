@echo off
echo Cleaning bin and obj folders...

for /d /r %%i in (bin,obj) do (
    echo Deleting %%i
    rmdir /s /q "%%i"
)

echo Done.