````markdown
# 📞 Phonebook Application (.NET 10)

A full-stack Phonebook Application built using **Vue.js, .NET 10 ASP.NET Core, PostgreSQL, Docker, and Nginx**.

## Architecture

```text
Browser
   ↓
Nginx (Vue.js Frontend)
   ↓  /api
.NET 10 ASP.NET Core Web API
   ↓
PostgreSQL
````

## Features

* User registration and login
* PostgreSQL-based authentication
* Protected API endpoints
* Add, view, update, and delete contacts
* User-specific contact ownership
* Search contacts
* Google-style numbered pagination
* Import contacts using CSV
* CSV column mapping and validation
* Export contacts as CSV
* PostgreSQL database storage
* Up to 1000 contacts for testing

## Run Locally

### Requirements

* Docker Desktop
* Git

Clone the repository:

```bash
git clone https://github.com/Harsh280705/phonebook_dotnet10.git
cd phonebook_dotnet10
```

Build and start the application:

```bash
docker compose up --build
```

Open the application:

```text
http://localhost
```

## Stop the Application

```bash
docker compose down
```

To rebuild after backend or frontend changes:

```bash
docker compose up --build
```

## Populate Fake Contacts

With the stack running:

```bash
docker compose exec backend dotnet Phonebook.Api.dll populate
```

This adds contacts until the database has approximately 1000 contacts. Existing contacts are preserved.

## Testing

### Backend API Tests

The backend API tests use an isolated `phonebook_test` database and do not modify the main application database.

Run the API tests:

```bash
docker compose --profile test run --rm api-tests
```

### Playwright End-to-End Tests

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

The Playwright tests cover:

* Authentication
* Contact CRUD operations
* CSV import
* CSV export
* Search
* Pagination

```
```
