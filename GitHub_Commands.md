# Основные команды Git для проекта TemplateGenerator

git add . && git commit -m "message" && git push

## 1. Инициализация репозитория

```bash
cd C:\_Work\VSCode\TemplateGenerator\TemplateGenerator
```

```bash
git init
```

```bash
git status
```

## 2. Настройка Git (если не настроен)

```bash
git config user.name "Антон Решетов"
```

```bash
git config user.email "your.email@example.com"
```

```bash
git config --list
```

## 3. Работа с файлами

```bash
git status
```

```bash
git diff
```

```bash
git diff Form1.cs
```

```bash
git add .
```

```bash
git add Form1.cs Form1.Designer.cs Program.cs TemplateGenerator.csproj
```

```bash
git add *.cs
```

```bash
git rm --cached имя_файла
```

## 4. Создание коммитов

```bash
git commit -m "Initial commit: Генератор экземпляров по шаблону"
```

```bash
git commit -m "v2.3: Добавлена поддержка XML и визуальное выделение тегов" -m "- Поддержка XML с обновлением атрибута NAME
- Визуальное выделение несовпадающих тегов красным
- Адаптивная панель кнопок
- Исправлены ошибки инициализации"
```

```bash
git commit -am "Обновление: исправлены ошибки"
```

```bash
git commit --amend -m "Новое сообщение для последнего коммита"
```

## 5. Работа с удаленным репозиторием (GitHub)

```bash
git remote add origin https://github.com/ваш_username/TemplateGenerator.git
```

```bash
git remote add origin git@github.com:ваш_username/TemplateGenerator.git
```

```bash
git remote -v
```

```bash
git push -u origin main
```

```bash
git push -u origin master
```

```bash
git push
```

```bash
git push origin main
```

## 6. Клонирование репозитория

```bash
git clone https://github.com/ваш_username/TemplateGenerator.git
```

```bash
git clone https://github.com/ваш_username/TemplateGenerator.git MyTemplateGenerator
```

## 7. Работа с ветками

```bash
git branch feature/xml-support
```

```bash
git checkout feature/xml-support
```

```bash
git checkout -b feature/xml-support
```

```bash
git branch
```

```bash
git branch -a
```

```bash
git checkout main
```

```bash
git merge feature/xml-support
```

```bash
git branch -d feature/xml-support
```

```bash
git push origin --delete feature/xml-support
```

## 8. Обновление и синхронизация

```bash
git fetch
```

```bash
git pull
```

```bash
git pull origin main
```

```bash
git pull --force
```

## 9. Отмена изменений

```bash
git checkout -- имя_файла
```

```bash
git checkout -- .
```

```bash
git reset --soft HEAD~1
```

```bash
git reset --hard HEAD~1
```

```bash
git revert HEAD
```

```bash
git push
```

```bash
git clean -fd
```

## 10. Просмотр истории

```bash
git log
```

```bash
git log --oneline
```

```bash
git log --oneline --graph --all
```

```bash
git show hash_коммита
```

```bash
git log -n 5
```

## 11. Для вашего конкретного проекта

```bash
cd C:\_Work\VSCode\TemplateGenerator\TemplateGenerator
```

```bash
git status
```

```bash
git add .
```

```bash
git commit -m "v2.3: Генератор экземпляров по шаблону

Новые функции:
- Поддержка XML с обновлением атрибута NAME
- Визуальное выделение несовпадающих тегов красным
- Адаптивная панель кнопок (FlowLayoutPanel)
- Белый цвет заголовка 'Детали замен по экземплярам'

Исправления:
- Ошибка инициализации таблицы замен (Object reference)
- Исправлен порядок создания колонок DataGridView
- Добавлена обработка исключений в конструкторе

Технические изменения:
- Создание колонок DataGridView вручную
- Добавлен FlowLayoutPanel для адаптивности кнопок
- Улучшена обработка ошибок в Program.cs"
```

```bash
git push origin main
```

```bash
git push -u origin main
```

## 12. Создание .gitignore

```bash
echo "# Visual Studio" > .gitignore
```

```bash
echo ".vs/" >> .gitignore
```

```bash
echo "bin/" >> .gitignore
```

```bash
echo "obj/" >> .gitignore
```

```bash
echo "*.user" >> .gitignore
```

```bash
echo "*.suo" >> .gitignore
```

```bash
echo "" >> .gitignore
```

```bash
echo "# Build results" >> .gitignore
```

```bash
echo "[Dd]ebug/" >> .gitignore
```

```bash
echo "[Rr]elease/" >> .gitignore
```

```bash
echo "x64/" >> .gitignore
```

```bash
echo "x86/" >> .gitignore
```

```bash
echo "" >> .gitignore
```

```bash
echo "# User-specific files" >> .gitignore
```

```bash
echo "*.rsuser" >> .gitignore
```

```bash
echo "*.suo" >> .gitignore
```

```bash
echo "*.user" >> .gitignore
```

Или скачать готовый .gitignore:

```bash
curl -o .gitignore https://raw.githubusercontent.com/github/gitignore/main/VisualStudio.gitignore
```

## 13. Полезные комбинации команд

```bash
git status && git add .
```

```bash
git commit -m "Сообщение" && git push
```

```bash
git status && git add . && git commit -m "Сообщение" && git push
```

## 14. Решение проблем

```bash
git add забытый_файл.cs
```

```bash
git commit --amend --no-edit
```

```bash
git commit --amend -m "Исправленное сообщение"
```

```bash
git reset --hard HEAD~1
```

```bash
git push --force
```

```bash
git pull
```

```bash
git add .
```

```bash
git commit -m "Разрешение конфликтов"
```

```bash
git push
```

## 15. Создание нового репозитория на GitHub (полный цикл)

```bash
git init
```

```bash
git add .
```

```bash
git commit -m "Initial commit: Генератор экземпляров по шаблону"
```

```bash
git remote add origin https://github.com/ваш_username/TemplateGenerator.git
```

```bash
git push -u origin main
```

## 16. Полный цикл для вашего проекта (если репозиторий уже есть)

```bash
cd C:\_Work\VSCode\TemplateGenerator\TemplateGenerator
```

```bash
git status
```

```bash
git add .
```

```bash
git commit -m "v2.3: Добавлена поддержка XML и визуальное выделение тегов

- Добавлена поддержка XML с обновлением атрибута NAME
- Визуальное выделение несовпадающих тегов красным
- Адаптивная панель кнопок (FlowLayoutPanel)
- Исправлена ошибка инициализации таблицы замен"
```

```bash
git push
```

## 17. Быстрые команды (шпаргалка)

```bash
# Статус
git status
```

```bash
# Добавить все
git add .
```

```bash
# Коммит
git commit -m "Сообщение"
```

```bash
# Отправить
git push
```

```bash
# Получить
git pull
```

```bash
# Ветки
git branch
```

```bash
# Переключиться на ветку
git checkout имя_ветки
```

```bash
# История
git log --oneline
```