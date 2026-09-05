from contextlib import asynccontextmanager

from fastapi import FastAPI
from fastapi.middleware.cors import CORSMiddleware
from sqlalchemy import text

from app.database.connection import Base, engine
from app.models.contact import Contact
from app.models.user import AuthSession, User
from app.routes.auth import router as auth_router
from app.routes.contacts import router as contacts_router


@asynccontextmanager
async def lifespan(app: FastAPI):
    Base.metadata.create_all(bind=engine)
    with engine.begin() as connection:
        connection.execute(text(
            "ALTER TABLE contacts ADD COLUMN IF NOT EXISTS "
            "user_id INTEGER REFERENCES users(id)"
        ))
        connection.execute(text(
            "CREATE INDEX IF NOT EXISTS ix_contacts_user_id "
            "ON contacts (user_id)"
        ))
    yield


app = FastAPI(
    title="Phonebook API",
    lifespan=lifespan
)


# Allow the Vue frontend to communicate with the FastAPI backend
origins = [
    "http://localhost:5173",
    "http://127.0.0.1:5173",
]


app.add_middleware(
    CORSMiddleware,
    allow_origins=origins,
    allow_credentials=True,
    allow_methods=["*"],
    allow_headers=["*"],
)


app.include_router(contacts_router)
app.include_router(auth_router)


@app.get("/")
def read_root():
    return {
        "message": "Phonebook API is running"
    }