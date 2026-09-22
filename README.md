# AppPicker

Небольшое приложение для Windows 10/11: показывает видимые окна, позволяет отметить нужные карточки пурпурным цветом и собрать выбранные окна в отдельный список.

## Запуск и сборка

Для запуска опубликованного self-contained файла .NET устанавливать не нужно. Для сборки исходников установите .NET 8 SDK, затем из папки проекта выполните:

```powershell
dotnet publish .\AppPicker.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o .\publish
```

Файл приложения появится в `publish\AppPicker.exe`. Запуск из исходников: `dotnet run --project .\AppPicker.csproj`.

## Приватность и демонстрация экрана

Настройка «Исключать собственное окно AppPicker из захвата» применяет официальный Windows API `SetWindowDisplayAffinity` с `WDA_EXCLUDEFROMCAPTURE` только к окну AppPicker. Функция доступна начиная с Windows 10 версии 2004; поведение зависит от версии Windows и способа захвата, абсолютной защиты от записи нет. Она не скрывает сторонние окна. Чтобы показать стороннюю программу без рабочего стола и других окон, используйте режим демонстрации отдельного окна в программе для трансляции.

## Технологии

C#, .NET 8, WPF, Win32 `EnumWindows` и `SetWindowDisplayAffinity`.
