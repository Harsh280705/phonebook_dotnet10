# 📞 Phonebook Application

A full-stack Phonebook Application built using **Vue.js, FastAPI, PostgreSQL, SQLAlchemy, Docker, and Nginx**.

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

## Testing

Run all Playwright tests:

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

