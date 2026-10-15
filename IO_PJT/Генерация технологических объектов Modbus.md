Структура данных для импорта в Scada показана в файле "ТехОбъекты_простой"
Где:
№: - не заполняется
Статус: -не заполняется
Раздел: TEHOBJ 
Марка: формируется из Loop или Tag, всегда начинается с _. А все тире заменяются на _. Например, 1115-MHS-00101A преобразуется в _1115_MHS_00101A
Тип объекта: - тип данных в скада:             
            new DataTypeMapping { SourceDataType = "BOOL", TargetDataType = "BOOL", IsMapped = true },
            new DataTypeMapping { SourceDataType = "8-Bit Signed", TargetDataType = "INT", IsMapped = true },
            new DataTypeMapping { SourceDataType = "8-Bit Unsigned", TargetDataType = "WORD", IsMapped = true },
            new DataTypeMapping { SourceDataType = "16-Bit Signed", TargetDataType = "INT", IsMapped = true },
            new DataTypeMapping { SourceDataType = "16-Bit Unsigned", TargetDataType = "WORD", IsMapped = true },
            new DataTypeMapping { SourceDataType = "32-Bit Signed", TargetDataType = "INT", IsMapped = true },
            new DataTypeMapping { SourceDataType = "32-Bit Unsigned", TargetDataType = "UDINT", IsMapped = true },
            new DataTypeMapping { SourceDataType = "32-Bit Floating", TargetDataType = "REAL", IsMapped = true },
            new DataTypeMapping { SourceDataType = "64-Bit Floating", TargetDataType = "REAL", IsMapped = true }
Также должна быть таблица чтобы задавать соотношения между типами данных в IO LIST и типами данных в Scada (в таблице технологических объектов для Scad)

Наименование: - Service в IO LIST
Описание:  -LOOP
Подпись: не заполняется
Номер: не заполняется
PLC переменная: не заполняется
Период архив: всегда равен 0
KKS: - Tag
Доп.параметр: -внести кратко информаицияоб адресе modbus (не более 50 символов)
Классификатор: - необходимо заполнять в ручную
Группа событий: - необходимо заполнять в ручную
КОНТРОЛЛЕР: - FCS
Адрес: всегда равен 0
№ ресурса или группа: -по умолчанию 1, но может  заполняться в ручную
Шаблон: всегда текст: простой




#### 2.5.1 Допустимые SCADA типы

| № | Тип | Описание |
|---|-----|----------|
| 1 | BOOL | Булевое значение |
| 2 | WORD | 16-битное беззнаковое целое |
| 3 | INT | 16-битное знаковое целое |
| 4 | UINT | 16-битное беззнаковое целое |
| 5 | UDINT | 32-битное беззнаковое целое |
| 6 | REAL | 32-битное число с плавающей точкой |


#### 2.5.4 Маппинг по умолчанию

| Исходный тип | SCADA тип |
|--------------|-----------|
| BOOL | BOOL |
| 8-Bit Signed | INT |
| 8-Bit Unsigned | WORD |
| 16-Bit Signed | INT |
| 16-Bit Unsigned | WORD |
| 32-Bit Signed | INT |
| 32-Bit Unsigned | UDINT |
| 32-Bit Floating | REAL |
| 64-Bit Floating | REAL |