# Документация парсера промышленных тегов Instrumentation

## Обзор

Парсер предназначен для разбора промышленных тегов приборов КИПиА в соответствии со стандартами ISA-101, KKS или пользовательскими форматами. Парсер автоматически определяет структуру тега, извлекает компоненты и валидирует их.

---

## Формат тега

### Общая структура

```
[Area][Separator][DeviceClass][Separator][Loop][TagNumber][Suffix]
```

### Компоненты

| Компонент | Описание | Тип данных | Длина | Обязательность | Пример |
| :--- | :--- | :--- | :--- | :--- | :--- |
| **Area** | Код площадки/завода/цеха | Цифры | 0-4 | Нет | `1234`, `42`, `5` |
| **Separator** | Разделитель | Символ | 1 | Да | `-`, `_`, `/`, `.` |
| **DeviceClass** | Класс прибора | Буквы A-Z | 1-5 | Да | `PDT`, `FT`, `TEMP` |
| **Separator** | Разделитель | Символ | 1 | Да | `-`, `_`, `/`, `.` |
| **Loop** | Номер технологического контура | Цифры | 1-5 | Да | `010`, `123`, `9999` |
| **TagNumber** | Порядковый номер прибора | Цифры | 0-3 | Нет | `01`, `5` |
| **Suffix** | Суффикс (модификатор) | Буквы/Цифры | 0-3 | Нет | `A`, `AB`, `X1` |

---

## Допустимые разделители

Парсер автоматически определяет разделитель из следующего списка (в порядке приоритета):

| Разделитель | Описание | Пример |
| :--- | :--- | :--- |
| `-` | Дефис | `1234-PDT-01001A` |
| `_` | Подчеркивание | `42_FT_123` |
| `/` | Слеш | `5/TT/001` |
| `.` | Точка | `1234.PDT.01001` |
| `:` | Двоеточие | `P:9999` |
| `;` | Точка с запятой | `FT;123` |
| `\|` | Вертикальная черта | `TEMP\|12345` |

---

## Правила парсинга

### 1. Определение Area

- Area определяется как последовательность цифр **перед первым разделителем**
- Может отсутствовать (тогда тег начинается с DeviceClass)
- Если Area отсутствует, первый разделитель стоит сразу после DeviceClass
- **Разделитель не может быть в начале строки**

### 2. Определение DeviceClass

- Определяется как последовательность букв **между первым и вторым разделителями**
- Если Area отсутствует — DeviceClass идет **до первого разделителя**
- Допустимые значения: 1-5 букв (A-Z, регистронезависимо)
- Всегда приводится к верхнему регистру

### 3. Определение Loop

- Определяется как последовательность цифр **после второго разделителя**
- Может идти сразу после DeviceClass (без разделителя)
- Отделяется от TagNumber и Suffix автоматически

### 4. Определение TagNumber

- Определяется как **следующие цифры после Loop**
- Может отсутствовать

### 5. Определение Suffix

- Определяется как **все остальные символы после TagNumber**
- Может отсутствовать
- Приводится к верхнему регистру

---

## Примеры тегов

### Валидные теги

| Тег | Area | DeviceClass | Loop | TagNumber | Suffix | Пояснение |
| :--- | :--- | :--- | :--- | :--- | :--- | :--- |
| `1234-PDT-01001A` | 1234 | PDT | 010 | 01 | A | Полный формат |
| `42-FT-123` | 42 | FT | 123 | | | Без TagNumber и Suffix |
| `P-9999` | | P | 9999 | | | Без Area |
| `TEMP-12345` | | TEMP | 12345 | | | Длинный DeviceClass |
| `LEVEL-123A` | | LEVEL | 123 | | A | Без Area, с Suffix |
| `1234-PDT-01001AB` | 1234 | PDT | 010 | 01 | AB | Длинный Suffix |
| `PDT_01001A` | | PDT | 010 | 01 | A | Разделитель `_` |
| `1234.PDT.01001` | 1234 | PDT | 010 | 01 | | Разделитель `.` |
| `1234PDT01001A` | 1234 | PDT | 010 | 01 | A | Без разделителя |
| `P9999` | | P | 9999 | | | Без разделителя |

### Невалидные теги

| Тег | Причина ошибки |
| :--- | :--- |
| `-P-9999` | Разделитель в начале строки |
| `123-` | Отсутствует Loop |
| `A1-123` | DeviceClass содержит цифры |
| `1234-TOOLONG-123` | DeviceClass > 5 символов |
| `1234-PDT-123456` | Loop > 5 символов |
| `1234-PDT-1234ABC` | Suffix > 3 символов |

---

## Использование

### Базовое использование

```csharp
using Instrumentation;

// Парсинг тега
var tag = InstrumentTag.Parse("1234-PDT-01001A");

// Доступ к компонентам
Console.WriteLine($"Area: {tag.Area}");           // "1234"
Console.WriteLine($"Device: {tag.DeviceClass}"); // "PDT"
Console.WriteLine($"Loop: {tag.Loop}");          // "010"
Console.WriteLine($"Tag: {tag.TagNumber}");      // "01"
Console.WriteLine($"Suffix: {tag.Suffix}");      // "A"
Console.WriteLine($"Separator: {tag.Separator}");// '-'
Console.WriteLine($"Full tag: {tag.FullTag}");   // "1234-PDT-01001A"
```

### Обработка ошибок

```csharp
try
{
    var tag = InstrumentTag.Parse("invalid-tag");
}
catch (ArgumentException ex)
{
    Console.WriteLine($"Ошибка парсинга: {ex.Message}");
}
```

### Пакетная обработка

```csharp
var tags = new[]
{
    "1234-PDT-01001A",
    "P-9999",
    "FT-123",
    "42-FT-123"
};

foreach (var t in tags)
{
    try
    {
        var tag = InstrumentTag.Parse(t);
        Console.WriteLine($"{t,-20} → {tag}");
    }
    catch (ArgumentException ex)
    {
        Console.WriteLine($"{t,-20} → Ошибка: {ex.Message}");
    }
}
```

---

## API Reference

### Класс `InstrumentTag`

#### Свойства

| Свойство | Тип | Описание |
| :--- | :--- | :--- |
| `Area` | `string` | Код площадки (0-4 цифры) |
| `DeviceClass` | `string` | Класс прибора (1-5 букв, верхний регистр) |
| `Loop` | `string` | Номер контура (1-5 цифр) |
| `TagNumber` | `string` | Номер прибора (0-3 цифры) |
| `Suffix` | `string` | Суффикс (0-3 символа, верхний регистр) |
| `FullTag` | `string` | Исходный тег |
| `Separator` | `char?` | Определенный разделитель (null если разделитель отсутствует) |

#### Методы

| Метод | Описание |
| :--- | :--- |
| `static InstrumentTag Parse(string tag)` | Парсит тег и возвращает объект `InstrumentTag` |
| `override string ToString()` | Возвращает строковое представление компонентов |

#### Исключения

| Исключение | Условие |
| :--- | :--- |
| `ArgumentException` | Тег пустой, null или имеет некорректный формат |

---

## Расшифровка DeviceClass

Парсер не выполняет расшифровку DeviceClass, но предоставляет сырые данные. Расшифровку можно реализовать отдельно:

```csharp
public static class DeviceClassMapper
{
    private static readonly Dictionary<string, string> Mapping = new()
    {
        { "PDT", "Pressure Differential Transmitter" },
        { "FT", "Flow Transmitter" },
        { "TT", "Temperature Transmitter" },
        { "LT", "Level Transmitter" },
        { "PT", "Pressure Transmitter" },
        { "TEMP", "Temperature" },
        { "LEVEL", "Level" }
    };

    public static string GetDescription(string deviceClass)
    {
        return Mapping.TryGetValue(deviceClass, out var desc) 
            ? desc 
            : deviceClass;
    }
}

// Использование
var tag = InstrumentTag.Parse("1234-PDT-01001A");
string description = DeviceClassMapper.GetDescription(tag.DeviceClass);
// "Pressure Differential Transmitter"
```

---

## Тестирование

### Пример юнит-тестов (NUnit)

```csharp
[TestFixture]
public class InstrumentTagTests
{
    [TestCase("1234-PDT-01001A", "1234", "PDT", "010", "01", "A")]
    [TestCase("P-9999", "", "P", "9999", "", "")]
    [TestCase("FT-123", "", "FT", "123", "", "")]
    [TestCase("42-FT-123", "42", "FT", "123", "", "")]
    [TestCase("TEMP-12345", "", "TEMP", "12345", "", "")]
    [TestCase("LEVEL-123A", "", "LEVEL", "123", "", "A")]
    [TestCase("1234-PDT-01001AB", "1234", "PDT", "010", "01", "AB")]
    [TestCase("PDT_01001A", "", "PDT", "010", "01", "A")]
    [TestCase("1234.PDT.01001", "1234", "PDT", "010", "01", "")]
    [TestCase("1234PDT01001A", "1234", "PDT", "010", "01", "A")]
    [TestCase("P9999", "", "P", "9999", "", "")]
    public void Parse_ValidTags_ReturnsCorrectParts(
        string tag, string area, string device, string loop, string tagNum, string suffix)
    {
        var result = InstrumentTag.Parse(tag);
        
        Assert.AreEqual(area, result.Area);
        Assert.AreEqual(device, result.DeviceClass);
        Assert.AreEqual(loop, result.Loop);
        Assert.AreEqual(tagNum, result.TagNumber);
        Assert.AreEqual(suffix, result.Suffix);
    }

    [Test]
    public void Parse_InvalidTags_ThrowsException()
    {
        Assert.Throws<ArgumentException>(() => InstrumentTag.Parse("-P-9999"));
        Assert.Throws<ArgumentException>(() => InstrumentTag.Parse(""));
        Assert.Throws<ArgumentException>(() => InstrumentTag.Parse("123-"));
        Assert.Throws<ArgumentException>(() => InstrumentTag.Parse("1234-TOOLONG-123"));
        Assert.Throws<ArgumentException>(() => InstrumentTag.Parse("1234-PDT-123456"));
        Assert.Throws<ArgumentException>(() => InstrumentTag.Parse(null));
    }
}
```

---

## Ограничения и допущения

1. **Регистр**: DeviceClass и Suffix всегда приводятся к верхнему регистру
2. **Разделитель**: Определяется автоматически по первому найденному символу из списка
3. **Валидация**: Проверяется только формат, не проверяется существование прибора в системе
4. **Производительность**: Парсер оптимизирован для пакетной обработки (использует скомпилированные Regex)

---

## История изменений

| Версия | Дата | Автор | Изменения |
| :--- | :--- | :--- | :--- |
| 1.0 | 2026-07-28 | System | Создание документации. Базовая реализация парсера. |

---

## Поддержка

При возникновении вопросов или необходимости расширения функциональности:

1. Обновить спецификацию
2. Обновить код парсера
3. Обновить юнит-тесты
4. Обновить документацию

---

## Лицензия

Данная документация является внутренней и предназначена для использования в проектах компании.