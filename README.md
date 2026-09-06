# 📞 Phonebook Application

A full-stack Phonebook Application built using **Vue.js, .NET 10, PostgreSQL, Docker, and Nginx**.

## Architecture

```text
Browser
   ↓
Nginx (Vue.js frontend)
   ↓  /api
.NET 10 ASP.NET Core Web API
   ↓
PostgreSQL
```

## Features

- User registration and login
- Add, view, update, and delete contacts
- Search contacts
- Google-style numbered pagination
- Import contacts using CSV
- Export contacts as CSV
- PostgreSQL database storage
- Up to 1000 contacts for testing

## Run Locally

### Requirements

- Docker Desktop
- Git

Clone the repository:

```bash
git clone https://github.com/Harsh280705/Phonebook_App.git
cd Phonebook_App
```

Build and start the application:

```bash
docker compose up --build
```

Open:

```text
http://localhost
```

## Stop the Application

```bash
docker compose down
```

Rebuild after backend or frontend changes:

```bash
docker compose up --build
```

## Populate Fake Contacts

With the stack running:

```bash
docker compose exec backend dotnet Phonebook.Api.dll populate
```

This adds contacts until the database has approximately 1000 rows. Existing contacts are left in place.

## Testing

### Backend API tests

The API tests use an isolated `phonebook_test` database on the same PostgreSQL instance. They do not modify production data.

```bash
docker compose --profile test run --rm api-tests
```

### Playwright end-to-end tests

From the `frontend` directory, with the application running at `http://localhost`:

```bash
npm run test:e2e
```

Run tests with the browser visible:

```bash
npm run test:e2e:headed
```

Run a specific test file:

```bash
npx playwright test tests/auth.spec.js
```

View the HTML test report:

```bash
npm run test:e2e:report
```

The Playwright tests cover authentication, contacts, import/export, and pagination.
