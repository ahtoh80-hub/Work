# Команды для работы с проектом TemplateGenerator в PowerShell (VS Code)

## 1. Базовые команды

```powershell
dotnet build
```

```powershell
dotnet run
```

```powershell
dotnet clean
```

```powershell
dotnet clean ; dotnet build
```

```powershell
dotnet build ; dotnet run
```

## 2. Публикация проекта

```powershell
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```

```powershell
dotnet publish -c Release -r win-x64 --self-contained false
```

```powershell
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o ./publish
```

## 3. Работа с пакетами NuGet

```powershell
dotnet add package EPPlus
```

```powershell
dotnet add package EPPlus --version 6.2.10
```

```powershell
dotnet restore
```

```powershell
dotnet remove package EPPlus
```

```powershell
dotnet list package
```

## 4. Создание нового проекта

```powershell
dotnet new winforms -n TemplateGenerator
```

```powershell
dotnet new winforms -n TemplateGenerator -f net8.0
```

## 5. Команды Git

```powershell
git status
```

```powershell
git add .
```

```powershell
git add Form1.cs Form1.Designer.cs Program.cs
```

```powershell
git commit -m "v2.3: Обновление программы"
```

```powershell
git push
```

```powershell
git pull
```

```powershell
git log --oneline
```

## 6. Полезные команды .NET

```powershell
dotnet --version
```

```powershell
dotnet --list-sdks
```

```powershell
dotnet --list-runtimes
```

```powershell
dotnet new sln -n TemplateGenerator
```

```powershell
dotnet sln add TemplateGenerator.csproj
```

## 7. Очистка и восстановление

```powershell
dotnet clean
```

```powershell
dotnet clean ; dotnet restore
```

## 8. Запуск с параметрами

```powershell
dotnet run --no-build
```

```powershell
dotnet run -c Release
```

```powershell
dotnet run -f net8.0
```

```powershell
dotnet build -c Release
```

```powershell
dotnet build -v detailed
```

```powershell
dotnet build -f net8.0-windows
```

## 9. Навигация по папкам

```powershell
cd C:\_Work\VSCode\TemplateGenerator\TemplateGenerator
```

```powershell
dir
```

```powershell
ls
```

```powershell
clear
```

```powershell
cls
```

## 10. Работа с файлами

```powershell
New-Item -ItemType File -Path .gitignore
```

```powershell
cat Program.cs
```

```powershell
Get-ChildItem -Recurse -Include *.cs | Select-String "Form1"
```

## 11. Быстрые скрипты PowerShell

### Создайте файл `build.ps1`:
```powershell
Write-Host "Очистка проекта..." -ForegroundColor Yellow
dotnet clean
Write-Host "Сборка проекта..." -ForegroundColor Yellow
dotnet build
if ($?) {
    Write-Host "Сборка успешно завершена!" -ForegroundColor Green
    Write-Host "Запуск программы..." -ForegroundColor Yellow
    dotnet run
} else {
    Write-Host "Ошибка сборки!" -ForegroundColor Red
}
```

Запуск:
```powershell
.\build.ps1
```

### Создайте файл `publish.ps1`:
```powershell
Write-Host "Публикация проекта..." -ForegroundColor Yellow
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
if ($?) {
    Write-Host "Публикация успешно завершена!" -ForegroundColor Green
    Write-Host "Файл находится в папке: bin\Release\net8.0-windows\win-x64\publish\" -ForegroundColor Cyan
} else {
    Write-Host "Ошибка публикации!" -ForegroundColor Red
}
```

Запуск:
```powershell
.\publish.ps1
```

### Создайте файл `full-cycle.ps1`:
```powershell
Write-Host "=== ПУЛЛ из GitHub ===" -ForegroundColor Cyan
git pull
Write-Host "=== ОЧИСТКА ===" -ForegroundColor Cyan
dotnet clean
Write-Host "=== СБОРКА ===" -ForegroundColor Cyan
dotnet build
if ($?) {
    Write-Host "=== ЗАПУСК ===" -ForegroundColor Cyan
    dotnet run
    Write-Host "=== КОММИТ ===" -ForegroundColor Cyan
    $commitMessage = Read-Host "Введите сообщение для коммита (или Enter для пропуска)"
    if ($commitMessage) {
        git add .
        git commit -m $commitMessage
        git push
        Write-Host "Изменения отправлены в GitHub!" -ForegroundColor Green
    }
} else {
    Write-Host "Ошибка сборки!" -ForegroundColor Red
}
```

Запуск:
```powershell
.\full-cycle.ps1
```

## 12. PowerShell функции для профиля

Добавьте в профиль PowerShell (`notepad $PROFILE`):

```powershell
function Build-Project {
    dotnet clean
    if ($?) {
        dotnet build
        if ($?) {
            Write-Host "Сборка успешна!" -ForegroundColor Green
        }
    }
}

function Run-Project {
    dotnet run
}

function Publish-Project {
    dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
    if ($?) {
        Write-Host "Публикация завершена!" -ForegroundColor Green
        Write-Host "Папка: bin\Release\net8.0-windows\win-x64\publish\" -ForegroundColor Cyan
    }
}

Set-Alias build Build-Project
Set-Alias run Run-Project
Set-Alias publish Publish-Project
```

После перезапуска PowerShell можно использовать:
```powershell
build
run
publish
```

## 13. Команды для CMD (если используете старый CMD)

```cmd
dotnet build
dotnet run
dotnet clean
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
git status
git add .
git commit -m "Сообщение"
git push
git pull
```

## 14. Полный цикл разработки (PowerShell)

```powershell
cd C:\_Work\VSCode\TemplateGenerator\TemplateGenerator
git status
git add .
git commit -m "Обновление: исправлены ошибки"
dotnet clean ; dotnet build
dotnet run
git push
```