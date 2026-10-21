# SCADA XML — полная структура и связи

## 1. Типы XML-файлов в проекте

В проекте встречаются **три типа XML**:

| Тип                           | Корневой тег   | Назначение                                                                              | Где используется                                                                                   |
| -------------------------------- | ------------------------- | ------------------------------------------------------------------------------------------------- | ----------------------------------------------------------------------------------------------------------------- |
| **Кадр SCADA**         | `<BufScada>`            | Описание кадра (страницы) со всеми примитивами             | Извлечение данных, DIAG AI/AO, TagsSearcher                                                       |
| **Логика POU**       | `<BufScadaPOUS>`        | Описание логики (ST или FBD) для одного или нескольких POU | Проверки ST↔FBD, извлечение тегов логики, извлечение перекладок |
| **Тех. объекты** | `<TEHOBJ>` (набор) | Справочник технологических объектов                              | `tags_searcher/tech_objects_loader.py`                                                                          |

Ниже — детально по каждому.

> ℹ️ Помимо XML проект работает и с **TXT-выгрузкой логики** (вкладка «Firebird export ISACARDS» →
> «Выгрузка логики POU»): тот же ST-код и FBD-рендер, но в плоском текстовом формате
> (`Programs_*.txt`), плюс встроенный блок справочника ISACARDS — см. `md/TXT_LOGIC_FORMAT.md`.
> XML-ветка извлечения перекладок описана ниже, TXT-ветка — в `md/EXCEL_REPORTS.md` §6.

---

## 2. Кадр SCADA — `<BufScada>`

### 2.1. Общий скелет

```xml
AGRAPH>><?xml version="1.0" encoding="UTF-8"?>
<BufScada>
  <Common ... />
  <GrObj>
    <OnePrim ...>...</OnePrim>
    ...
  </GrObj>
  <ISAOBJSINFO>
    <rec ID="..." Info="..."/>
  </ISAOBJSINFO>
  <ISACARDSINFO>
    <rec ID="..." Info="..." .../>
  </ISACARDSINFO>
  <PAGEMSINFO>
    <rec ID="..." Info="..."/>
  </PAGEMSINFO>
  <GRPAGESINFO>
    <rec ID="..." FullName="..."/>
  </GRPAGESINFO>
  <Pages>
    <OnePage>
      <ID>...</ID>
      <NAME>...</NAME>
      <SUBPAGES>...</SUBPAGES>
    </OnePage>
  </Pages>
</BufScada>
```

> **Важно:** файл может начинаться с мусора (`AGRAPH>>`) до `<?xml ... ?>`. Парсер проекта (`read_file_content`) обрезает всё до `<?xml` и удаляет управляющие символы.

### 2.2. `<Common>` — общие параметры кадра

```xml
<Common VER="29"
        PageMode="1"
        PageID="5160"
        Project="otpscadabases:c:\TAProject\...\SCADABD.GDB"
        isCut="false"
        isFFB="false"
        ControllerTypeName="TENIX-CPU715"
        ControllerID="189285"
        ResuorceID="627"/>
```

| Атрибут         | Смысл                        | Значения                                 |
| ---------------------- | --------------------------------- | ------------------------------------------------ |
| `VER`                | Версия формата       | обычно`29`                               |
| `PageMode`           | Режим страницы       | `1` (простой), `4` (логика/FBD) |
| `PageID`             | ID страницы               | число                                       |
| `Project`            | Путь к БД SCADA            | строка                                     |
| `isCut`              | Флаг «обрезанная» | `true` / `false`                             |
| `isFFB`              | Флаг FFB-логики         | `true` / `false`                             |
| `ControllerTypeName` | Тип ПЛК                     | `TENIX-CPU715`, `TENIX-CPU850`               |
| `ControllerID`       | ID контроллера         | число                                       |
| `ResuorceID`         | ID ресурса                 | число                                       |

### 2.3. `<GrObj>` — графические объекты кадра

```xml
<GrObj>
  <OnePrim SourceT11ID="894009"
           X="0" Y="0" WIDTH="1200" HEIGHT="448"
           OBJTYPE="8"
           GRNUM="0"
           DRAWTYPE="0"
           PenParams="1" PenColor="0"
           BrushColor="16777215" GradColor="536870911"
           ScriptName="">
    <PARAMS>[MODE]=-1
[TEXT]=
[FONTID]=0
[USERFONT]=8;Arial;0;0;
</PARAMS>
    <ObjMSID>3654</ObjMSID>
    <CardID>218245</CardID>
  </OnePrim>
  <OnePrim ...>...</OnePrim>
</GrObj>
```

| Атрибут / тег                                    | Смысл                                                                                                        |
| ---------------------------------------------------------- | ----------------------------------------------------------------------------------------------------------------- |
| `SourceT11ID`                                            | ID источника (шаблона)                                                                            |
| `X`, `Y`, `WIDTH`, `HEIGHT`                        | Геометрия примитива                                                                             |
| `OBJTYPE`                                                | Тип объекта (8 — панель/текст, 31 — тег, 34/35/36/38 — блоки FBD)                 |
| `GRNUM`, `DRAWTYPE`                                    | Служебные                                                                                                |
| `PenParams`, `PenColor`, `BrushColor`, `GradColor` | Стили отрисовки                                                                                     |
| `<PARAMS>`                                               | Многострочный текст параметров (`[MODE]`, `[TEXT]`, `[FONTID]`, `[USERFONT]`) |
| `<ObjMSID>`                                              | ID объекта MS (для неподвязанных объектов)                                         |
| `<CardID>`                                               | ID карты (для тегов)                                                                                 |

> **Порядок элементов внутри `OnePrim` важен:** `PARAMS → ObjMSID → CardID`.

### 2.4. `<ISAOBJSINFO>` — справочник объектов ISA

```xml
<ISAOBJSINFO>
  <rec ID="885" Info="DDR_v1"/>
  <rec ID="17611" Info="QUAL_STAT"/>
</ISAOBJSINFO>
```

| Атрибут | Смысл                                                             |
| -------------- | ---------------------------------------------------------------------- |
| `ID`         | ID объекта                                                      |
| `Info`       | Имя объекта ISA (например,`DDR_v1`, `QUAL_STAT`) |

Используется FBD-логикой: блоки ссылаются на этот ID через `IsaObjId`.

### 2.5. `<ISACARDSINFO>` (или `<CARDSINFO>`) — справочник карт/тегов

Ключевая секция: хранит **все теги** кадра.

```xml
<ISACARDSINFO>
  <rec ID="575277"
       Info="_IO_IU46_0"
       IsRetain="-1"
       Name="[42] CDI32 > канал 0"
       SSize="0"
       KLPath=""/>
  <rec ID="672880"
       Info="_1110_FVZ_10301B"
       IsRetain="-1"
       Name="Ethylene into R-101"
       SSize="0"
       KLPath="РСУ\EBSM\DI0010-01R"/>
</ISACARDSINFO>
```

| Атрибут | Смысл                                                        | Пример                         |
| -------------- | ----------------------------------------------------------------- | ------------------------------------ |
| `ID`         | Уникальный ID карты                                | `575277`                           |
| `Info`       | Полное имя объекта (тег или драйвер) | `_IO_IU46_0`, `_1110_FVZ_10301B` |
| `CardInfo`   | (иногда) альтернативный путь              | `2/CPU/1/_TAG/(AI16H)`             |
| `IsRetain`   | Флаг сохранения                                     | `-1` / `1` / `0`               |
| `Name`       | Отображаемое имя                                   | `[42] CDI32 > канал 0`        |
| `SSize`      | Размер                                                      | `0`                                |
| `KLPath`     | Путь классификатора                             | `РСУ\EBSM\DI0010-01R`           |

> **Ключевое отличие в проекте:** иногда тег берётся из `Info`, иногда из `CardInfo`. Парсеры проекта умеют работать с обоими вариантами.

### 2.6. `<PAGEMSINFO>` — описание объектов-страниц

```xml
<PAGEMSINFO>
  <rec ID="3654" Info="2//(AI_DIAG16_AD3v1_kvit)/(8x_AI_16_DIAG)"/>
  <rec ID="4730" Info="2//(AD3_v2)/(AD3v2_Diag)"/>
  <rec ID="4731" Info="2//(AD3_v2)/(AD3v2_Diag_LightBlu)"/>
</PAGEMSINFO>
```

| Атрибут | Смысл                                               |
| -------------- | -------------------------------------------------------- |
| `ID`         | ID (совпадает с`ObjMSID` примитива) |
| `Info`       | Строка вида`2//(Тип)/(Шаблон)`      |

Используется для **определения неподвязанных объектов**: если `Info` объекта **не содержит** графических ключевых слов (см. `GRAPHIC_KEYWORDS`), объект считается неподвязанным.

### 2.7. `<GRPAGESINFO>` — полные пути страниц

```xml
<GRPAGESINFO>
  <rec ID="19980" FullName="РСУ\EBSM\Кадры\AI\Module1"/>
</GRPAGESINFO>
```

| Атрибут | Смысл                       |
| -------------- | -------------------------------- |
| `ID`         | ID страницы              |
| `FullName`   | Полный путь кадра |

Используется для построения пути при отображении иерархии.

### 2.8. `<Pages>` — иерархия страниц (кадров)

```xml
<Pages>
  <OnePage>
    <ID>19980</ID>
    <NAME>Module1</NAME>
    <OnePrim ...>...</OnePrim>
    <SUBPAGES>
      <OnePage>
        <ID>19981</ID>
        <NAME>Submodule1</NAME>
        <OnePrim ...>...</OnePrim>
      </OnePage>
    </SUBPAGES>
  </OnePage>
</Pages>
```

| Тег         | Смысл                                                                                |
| -------------- | ----------------------------------------------------------------------------------------- |
| `<ID>`       | ID страницы (ссылается на`GRPAGESINFO`)                              |
| `<NAME>`     | Имя кадра                                                                         |
| `<OnePrim>`  | Примитивы**данного** кадра (вложенные в `OnePage`) |
| `<SUBPAGES>` | Вложенные страницы                                                       |

> **Важно:** примитивы могут быть как прямо в `<OnePage>`, так и в подсекциях. Парсер `collect_prims` рекурсивно обходит всё, кроме `<SUBPAGES>`.

---

## 3. Логика POU — `<BufScadaPOUS>`

### 3.1. Общий скелет

```xml
AGRAPH>><?xml version="1.0" encoding="UTF-8"?>
<BufScadaPOUS>
  <Common ... />
  <OnePOU NAME="..." isFBD="0" ...>
    <STCODE>...текст ST-кода...</STCODE>
  </OnePOU>
  <OnePOU NAME="..." isFBD="1" ...>
    <ISAGraf>
      <Blocks>
        <Block GROBJTYPE="31" Info="_TAG.Z2" T11ID="2136664">
          <Graphics X="650" Y="1130" WIDTH="170" HEIGHT="20"/>
          <Params T11Text=".Z2" CI="1" CO="1" VMODE="0"
                  IsaObjId="885" cardId="672880">
            <IV>6(FALSE),TRUE,1,2(FALSE),0,6(FALSE)</IV>
          </Params>
        </Block>
      </Blocks>
      <Gotos/>
      <Links>
        <Link ...>
          <PointList PL="(614,1140);(650,1140);"/>
          <FirstPoint FP="2136606|False|Result|0,0,0,0"/>
          <LastPoint  LP="2136664|True|0|0,0,0,0"/>
        </Link>
      </Links>
    </ISAGraf>
  </OnePOU>
</BufScadaPOUS>
```

> `BufScadaPOUS` может быть **обрезан** до `</POUS>` — в проекте есть функция `truncate_to_pous_end`, которая восстанавливает корневой тег.

### 3.2. `<OnePOU>` — программный блок

| Атрибут | Смысл                                    |
| -------------- | --------------------------------------------- |
| `NAME`       | Имя POU                                    |
| `isFBD`      | `0` — ST-код, `1` — FBD-логика |

### 3.3. ST-часть — `<STCODE>`

```xml
<OnePOU NAME="Module1_PRG" isFBD="0">
  <STCODE>
_TAG.Xin := _IO_IU0_0.ValueDINT;
_TAG.Xs  := _IO_IU0_0.Status;
  </STCODE>
</OnePOU>
```

Теги извлекаются регуляркой `\b(_[A-Za-z0-9_]+|B01[A-Za-z0-9_]*)\b`.

### 3.4. FBD-часть — `<Block>` + `<Links>`

#### `<Block>` — функциональный блок

| Атрибут / тег | Смысл                                                                                                                                   |
| ----------------------- | -------------------------------------------------------------------------------------------------------------------------------------------- |
| `GROBJTYPE`           | Тип блока (31 — тег, 34/35 — константы/логика, 36 — функция, 38 — вход/выход драйвера) |
| `Info`                | Текстовое описание (имя тега или пояснение)                                                              |
| `T11ID`               | Уникальный ID блока                                                                                                           |
| `<Graphics>`          | Координаты и размеры                                                                                                       |
| `<Params>`            | Параметры:`T11Text`, `CI` (кол-во входов), `CO` (кол-во выходов), `VMODE`, `IsaObjId`, `cardId`  |
| `<IV>`                | Внутренние значения (для блоков-констант)                                                                 |

#### `<Links>` — связи между блоками

```xml
<Link Negative="false" asPointer="false" UserEdit="false"
      Color="0" GetBitNum="-1" ConvertTo="0">
  <PointList PL="(614,1140);(650,1140);"/>
  <FirstPoint FP="2136606|False|Result|0,0,0,0"/>
  <LastPoint  LP="2136664|True|0|0,0,0,0"/>
</Link>
```

| Элемент            | Формат                               | Смысл                      |
| ------------------------- | ------------------------------------------ | ------------------------------- |
| `<PointList PL="...">`  | `(x1,y1);(x2,y2);...`                    | Координаты линии |
| `<FirstPoint FP="...">` | `T11ID\|Bool\|Порт\|Параметры` | Исходный блок       |
| `<LastPoint  LP="...">` | `T11ID\|Bool\|Порт\|Параметры` | Целевой блок         |

**Поля `FP`/`LP` (разделены `|`):**

1. `T11ID` блока — ссылка на `<Block T11ID="...">`
2. `True/False` — флаг
3. Порт (`Result`, `QUAL`, `0`, `1`, ...)
4. `0,0,0,0` — служебные параметры

### 3.5. Как извлечь теги из FBD

В проекте используется стратегия «плоского» поиска:

1. **Сначала** собираются все пары `Info="..."` + `T11ID="..."` — строится карта `{T11ID: Info}`.
2. **Затем** ищутся все `FP="..."` и `LP="..."` — каждая связь даёт пару `(FP.T11ID, LP.T11ID)`.
3. Через карту восстанавливаются имена: `FP.Info` и `LP.Info`.
4. Из `Info` извлекается тег регуляркой.
5. Для DO/DI: если на FirstPoint стоит блок-«прокладка» (`NOT`, `AND`, `OR`, `QUAL_STAT`, `MOVE`, `SEL`, константа и т.п.) — рекурсивно поднимаемся вверх по цепочке до реального тега.

Это **устойчиво к порядку** `Info` и `T11ID` в файле.

### 3.6. Список блоков-«прокладок» (`PASS_THROUGH`)

Используется в `DOFBDCheckLogic`:

```
NOT, AND, OR, XOR, QUAL_STAT, MOVE, SEL, LIMIT, MIN, MAX,
TON, TOF, TP, RS, SR, R_TRIG, F_TRIG, CTU, CTD,
FALSE, TRUE,
<, >, <>, <=, >=, =,
<, >, <>, <=, >=
```

Плюс числовые константы и литералы времени/даты (`T#`, `D#`, `TOD#`, `DT#`).

---

## 4. XML технологических объектов — `<TEHOBJ>`

Используется в `tags_searcher/tech_objects_loader.py`.

```xml
<TEHOBJS>
  <TEHOBJ MARKA="_1110_FVZ_10301B"
          OBJTYPENAME="Задвижка"
          PLCNAME="1000_D_SC_B01"
          KKS="10FVZ10301B"
          KLASSNAME="Арматура"
          TEMPLATENAME="FVZ_v1"
          DISC="..."
          NAME="Ethylene into R-101"
          EVKLASSNAME="..."
          OBJSIGN="..."
          TEMPLATEPREFIX="FVZ_">
    <ISACARDS>
      <IsaCard TEMPLATEPREFIX="FVZ_"/>
      <IsaCard TEMPLATEPREFIX="_FVZ"/>
    </ISACARDS>
  </TEHOBJ>
  ...
</TEHOBJS>
```

| Атрибут                      | Смысл                            | Использование                                      |
| ----------------------------------- | ------------------------------------- | --------------------------------------------------------------- |
| `MARKA`                           | Технологический тег | ключ для сравнения с тегами кадров |
| `OBJTYPENAME`                     | Тип объекта                 | фильтр в загрузчике                            |
| `PLCNAME`                         | Имя ПЛК                         | фильтр                                                    |
| `KKS`                             | KKS-код                            | фильтр                                                    |
| `KLASSNAME`                       | Класс                            | фильтр                                                    |
| `TEMPLATENAME`                    | Шаблон                          | фильтр                                                    |
| `DISC`                            | Описание                      | —                                                              |
| `NAME`                            | Имя                                | —                                                              |
| `EVKLASSNAME`                     | Класс события             | —                                                              |
| `OBJSIGN`                         | Подпись                        | фильтр                                                    |
| `TEMPLATEPREFIX`                  | Префикс шаблона         | нормализация тегов при экспорте     |
| `ISACARDS/IsaCard@TEMPLATEPREFIX` | Префиксы                      | список суффиксов для`normalize_tag`         |

### Как используется `TEMPLATEPREFIX`

```python
normalize_tag(tag, prefix_set)
```

Если `tag` заканчивается на один из префиксов из `prefix_set` — суффикс отрезается.

**Пример:**

```
tag       = "_1110_FVZ_10301B_FVZ"
prefixes  = {"FVZ", "_FVZ"}
результат = "_1110_FVZ_10301B"
```

---

## 5. Полная карта связей (картинка в тексте)

```
┌─────────────────────────────────────────────────────────────────┐
│                         <BufScada>                              │
│                                                                 │
│  <Common>  ─── тип контроллера, ID страницы, путь к проекту    │
│                                                                 │
│  <GrObj>                                                        │
│    └── <OnePrim CardID="X" ObjMSID="Y">                         │
│              │            │                                     │
│              │            └──► <PAGEMSINFO ID="Y">              │
│              │                      └─ Info → графика/не графика│
│              │                                                  │
│              └──► <CARDSINFO ID="X">                            │
│                        └─ Info / CardInfo → ТЕГ                 │
│                                                                 │
│  <Pages>/<OnePage>                                              │
│    ├── <ID> ──► <GRPAGESINFO ID>  ── FullName (путь)            │
│    ├── <NAME>                                                   │
│    ├── <OnePrim ...>  (примитивы кадра)                         │
│    └── <SUBPAGES>/<OnePage> ...  (вложенные кадры)              │
│                                                                 │
│  <ISAGraf>                                                      │
│    ├── <Blocks>/<Block T11ID="A" Info="_TAG">                   │
│    │        └── <Params IsaObjId="..." cardId="...">            │
│    └── <Links>/<Link>                                           │
│             ├── <FirstPoint FP="A|...">  ──► Block T11ID=A      │
│             └── <LastPoint  LP="B|...">  ──► Block T11ID=B      │
│                                                                 │
└─────────────────────────────────────────────────────────────────┘
```

---

## 6. Алгоритм парсинга кадра (по шагам)

### 6.1. Извлечение тегов кадра

```
1. Открыть XML → найти <Pages>
2. Для каждого <OnePage>:
     a. Рекурсивно собрать все <OnePrim> (кроме SUBPAGES)
     b. Для каждого <OnePrim>:
          - взять <CardID>
          - найти <CARDSINFO><rec ID=CardID>
          - извлечь тег из CardInfo или Info
          - если тег начинается с "_" или "B01" → добавить в список
     c. Перейти к <SUBPAGES>
```

### 6.2. Извлечение неподвязанных объектов

```
1. Для каждого <OnePrim>:
     - если CardID отсутствует или = 0:
         - взять <ObjMSID>
         - найти <PAGEMSINFO><rec ID=ObjMSID>
         - проверить Info на графические ключевые слова
         - если НЕ графика → добавить в orphan_infos
```

### 6.3. Извлечение иерархии

```
1. Рекурсивный обход <Pages>/<OnePage>
2. Для каждого <OnePage>:
     - <ID> → полный путь из <GRPAGESINFO>
     - если нет в GRPAGESINFO — построить путь как parent_path\NAME
```

### 6.4. Извлечение тегов из логики

```
Если <OnePOU isFBD="0">:
    взять текст из <STCODE>, применить regex
Если <OnePOU isFBD="1">:
    a. Собрать карту {T11ID: Info} из всех <Block>
    b. Собрать все пары (FP.T11ID, LP.T11ID) из <Link>
    c. Для каждого ID получить Info, извлечь тег
    d. Пропустить блоки-прокладки (NOT, AND, QUAL_STAT, ...)
    e. Для DO-цепочки — подняться вверх до реального тега
```

### 6.5. Извлечение перекладок из ST-кода (PerekladkiExtractor)

Распознаются строки:

| Паттерн                                            | Тип | Что даёт                                  |
| --------------------------------------------------------- | ------ | ------------------------------------------------ |
| `TAG.Xin := _IO_IUn_M.ValueDINT;`                       | AI-715 | tag, driver=`_IO_IUn`, channel=`M`           |
| `TAG.Xs := _IO_IUn_M.Status;`                           | AI-715 | tag, driver, channel                             |
| `TAG.Xin := QUAL_STAT(_IO_In_AI16H_M_VAL.Measurement);` | AI-850 | tag, driver=`_IO_In_AI16H`, channel=`M`      |
| `_IO_QUn_M.ValueDINT := REAL_TO_DINT(TAG.OUT, ...);`    | AO     | tag, driver=`_IO_QUn`, channel=`M`           |
| `MODULE_AOC4H(TAG1.Stat, TAG2.Stat, ...)`               | AO     | module, tags → каналы по порядку |
| `TAG.iN := _IO_IUn_M.Value;`                            | DI-715 | tag, driver, channel=`N`                       |
| `TAG.sts := _IO_IUn_M.Valid;`                           | DI-715 | tag, driver, channel=`M`                       |
| `TAG.iN := _IO_In_DI32_M_VAL.Measurement;`              | DI-850 | tag, driver, channel=`N`                       |
| `_IO_QUn_M.Value := TAG._N;`                            | DO-715 | tag, driver, channel=`N`                       |
| `_IO_Qn_DO32P_M_VAL.Measurement := TAG._N;`             | DO-850 | tag, driver, channel=`N`                       |
| `MODULE_AI16H((*00*)TAG1.Stat, (*01*)TAG2.Stat, ...);`  | AI-16H | module, tag, channel=`00`                      |

---

## 7. Графические ключевые слова (GRAPHIC_KEYWORDS)

Если `Info` объекта из `<PAGEMSINFO>` содержит любое из этих слов (регистронезависимо) — объект **графика**, а не неподвязанный тег.

```python
GRAPHIC_KEYWORDS = [
    "Арматура", "Диагностика", "Емкости",
    "Компрессор.Части", "Компроссора", "Механизмы",
    "Насосы обвязки", "Печи", "План расположения",
    "Фильтры", "Трубы вертик", "Трубы горизонт",
    "Указатели", "Вогнутый прямоугольник_copy",
    "Выпуклый прямоугольник", "горелка", "Факел горелки"
]
```

Редактируется через UI (`TagsSearcherTab.edit_exceptions`).

---

## 8. Кодировки и BOM

При чтении XML-файлов:

1. Если файл начинается с `\xef\xbb\xbf` (BOM UTF-8) — BOM отрезается.
2. Кодировка определяется из `<?xml ... encoding="..."?>`.
3. Если кодировка не указана — используется `utf-8`.
4. Управляющие символы (`\x00-\x1f`, кроме `\t\n\r`) удаляются.
5. Всё до `<?xml` отрезается.

**Функция:** `tags_searcher.logic.read_file_content(file_path)`.

---

## 9. Обработка «обрезанных» файлов

Файлы логики могут быть **обрезаны** после `</POUS>`. Функция `DIFBDCheckLogic.truncate_to_pous_end`:

1. Находит `</POUS>`.
2. Отрезает всё после него.
3. Добавляет закрывающий `</BufScadaPOUS>`.

---

## 10. Частые проблемы и что с ними делать

| Проблема                                   | Причина                                                 | Решение                                                                                     |
| -------------------------------------------------- | -------------------------------------------------------------- | -------------------------------------------------------------------------------------------------- |
| `ParseError`                                     | Некорректный XML                                   | Использовать`read_file_content` (чистит управляющие символы) |
| Нет тегов                                  | Тег в`CardInfo`, а не в `Info`                     | Парсить оба атрибута                                                             |
| Дубли тегов                              | Один тег на нескольких примитивах | Использовать`seen_tags` (set)                                                        |
| Пустые страницы                      | Нет`OnePrim`                                              | Проверять`if not page['tags'] and not orphans`                                          |
| Разные каналы у одного тега | Конфликт в XML                                        | Логировать в`conflicts`, брать первый                                      |
| BOM в начале                                | UTF-8 с BOM                                                   | Отрезать`\xef\xbb\xbf`                                                                   |
| Мусор до`<?xml`                           | `AGRAPH>>` и т.п.                                         | Искать`<?xml` и обрезать                                                          |
| Файл обрезан                            | Нет закрывающих тегов                       | `truncate_to_pous_end` + восстановить корень                                   |

---

## 11. Быстрые ориентиры: какой парсер где

| Задача                                    | Модуль / функция                                    |
| ----------------------------------------------- | ---------------------------------------------------------------- |
| Чтение XML с BOM                         | `tags_searcher.logic.read_file_content`                        |
| Иерархия страниц кадра      | `tags_searcher.logic.extract_hierarchy_from_xml`               |
| Плоский список страниц      | `tags_searcher.logic.flatten_pages`                            |
| Теги из CardInfo                          | `tags_searcher.logic.extract_tag_from_cardinfo`                |
| Неподвязанные объекты       | `tags_searcher.logic.find_unfound_objects`                     |
| Теги логики (полные + POU)      | `tags_searcher.logic.extract_pou_map_from_logic_xml`           |
| Нормализация тега               | `tags_searcher.logic.normalize_tag`                            |
| Карта тег → кадры                 | `tags_searcher.logic.map_tech_to_frames`                       |
| Теги DIAG AI из XML                       | `logics.diag_ai_check_logic.extract_tags_from_xml`             |
| Теги DIAG AO из XML                       | `logics.diag_ao_check_logic.extract_tags_from_xml`             |
| DI FBD (пары FP/LP)                         | `logics.di_fbd_check_logic.process_single_file`                |
| DO FBD (пары FP/LP)                         | `logics.do_fbd_check_logic.process_single_file`                |
| Извлечение перекладок (ST)  | `tabs.Perekladki.perekladki_extractor_tab.PerekladkiExtractor` |
| Извлечение перекладок (FBD) | `PerekladkiExtractor._parse_fbd`                               |
| Обрезка до`</POUS>`                  | `DIFBDCheckLogic.truncate_to_pous_end`                         |

---

## 12. Итоговая шпаргалка «одним взглядом»

- **Кадр** = `<Pages>/<OnePage>` + `<GrObj>/<OnePrim>` + `<CARDSINFO>` + `<PAGEMSINFO>` + `<GRPAGESINFO>`.
- **Примитив** ссылается на карту через `<CardID>` → получаем тег.
- **Примитив** ссылается на объект MS через `<ObjMSID>` → определяем неподвязанность.
- **Страница** ссылается на путь через `<ID>` → `<GRPAGESINFO/FullName>`.
- **Логика POU** = `<OnePOU>` с `isFBD="0"` (ST) или `isFBD="1"` (FBD).
- **FBD** = `<Block T11ID Info>` + `<Link FP LP>`; связь восстанавливается через карту `T11ID → Info`.
- **Тех. объект** = `<TEHOBJ MARKA ...>`; тег = `MARKA`.
- **Нормализация** = отрезать суффикс из `TEMPLATEPREFIX` от тега.
- **Графика** = примитивы, чей `Info` содержит ключевые слова из `GRAPHIC_KEYWORDS`.
- **Прокладки** = блоки `NOT`, `AND`, `OR`, `QUAL_STAT`, константы, таймеры — не теги, их нужно пропускать при разрешении DO-цепочек.
