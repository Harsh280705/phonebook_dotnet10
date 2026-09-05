````md
# 📞 Phonebook Application

A full-stack Phonebook Application built using **Vue.js, FastAPI, PostgreSQL, SQLAlchemy, Docker, and Nginx**. Users can add, view, update, delete, and search contacts.

The application includes pagination for large datasets and a Faker-based script to populate the database with up to 1000 fake contacts for testing.

## Setup

### Requirements

- Docker Desktop
- Git

Clone the repository:

```bash
git clone https://github.com/Harsh280705/Phonebook_App.git
cd Phonebook_App
````

## Run Using Docker

Build and start the application:

```bash
docker compose up --build
```

Open the application:

```text
http://localhost
```

Nginx acts as a reverse proxy, serving the frontend and forwarding API requests to the FastAPI backend.

## Populate Fake Data

To populate the database with up to 1000 fake contacts:

```bash
docker compose exec backend python populate_contacts.py
```

The script checks the existing number of contacts and only adds the required number to reach 1000.

## Testing

Test the application using:

* Frontend UI
* FastAPI Swagger UI

API documentation:

```text
http://localhost/docs
```

Available APIs:

* `GET /contacts/`
* `POST /contacts/`
* `GET /contacts/{id}`
* `PUT /contacts/{id}`
* `DELETE /contacts/{id}`

## Stop the Application

```bash
docker compose down
```

```
```
