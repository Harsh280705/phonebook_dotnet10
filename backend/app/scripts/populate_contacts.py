from faker import Faker

from app.database.connection import Base, SessionLocal, engine
from app.models.contact import Contact


TARGET_COUNT = 1000


def populate_contacts():
    Base.metadata.create_all(bind=engine)

    fake = Faker()
    db = SessionLocal()

    try:
        existing_count = db.query(Contact).count()
        records_needed = max(TARGET_COUNT - existing_count, 0)

        if records_needed == 0:
            print(f"Database already contains {existing_count} contacts.")
            return

        existing_phones = {
            phone for (phone,) in db.query(Contact.phone_number).all()
        }
        existing_emails = {
            email for (email,) in db.query(Contact.email).all() if email
        }

        contacts = []

        while len(contacts) < records_needed:
            phone_number = f"+1{fake.random_int(2000000000, 9999999999)}"
            email = fake.safe_email()

            if phone_number in existing_phones or email in existing_emails:
                continue

            existing_phones.add(phone_number)
            existing_emails.add(email)

            contacts.append(
                Contact(
                    name=f"{fake.first_name()} {fake.last_name()}",
                    phone_number=phone_number,
                    email=email,
                    address=fake.address().replace("\n", ", ")[:255]
                )
            )

        db.add_all(contacts)
        db.commit()
        print(f"Added {records_needed} contacts. Total contacts: {TARGET_COUNT}.")
    except Exception:
        db.rollback()
        raise
    finally:
        db.close()


if __name__ == "__main__":
    populate_contacts()