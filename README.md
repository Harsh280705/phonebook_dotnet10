# 📞 Phonebook Application

A full-stack Phonebook Application built using **Vue.js, FastAPI, PostgreSQL, SQLAlchemy, and Docker**. Users can add, view, update, and delete contacts through a simple web interface.

## Setup

### Requirements
- Docker Desktop
- Git

Clone the repository:

```bash
git clone <your-repository-url>
cd Phonebook-app

Run Using Docker

Build and start the application:

docker compose up --build

Open:

Frontend: http://localhost:5173
Backend: http://localhost:8000
API Docs: http://localhost:8000/docs

To stop:

docker compose down
Testing

Test the application using the frontend or FastAPI Swagger UI:

http://localhost:8000/docs

Available APIs:

GET /contacts/
POST /contacts/
GET /contacts/{id}
PUT /contacts/{id}
DELETE /contacts/{id}