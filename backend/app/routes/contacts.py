from fastapi import APIRouter, Depends, HTTPException, status
from sqlalchemy.orm import Session
from sqlalchemy.exc import IntegrityError

from app.database.connection import get_db
from app.models.contact import Contact
from app.schemas.contact import (
    ContactCreate,
    ContactResponse,
    ContactUpdate
)


router = APIRouter(
    prefix="/contacts",
    tags=["Contacts"]
)


# CREATE CONTACT
@router.post(
    "/",
    response_model=ContactResponse,
    status_code=status.HTTP_201_CREATED
)
def create_contact(
    contact: ContactCreate,
    db: Session = Depends(get_db)
):

    # Check duplicate phone number
    existing_phone = (
        db.query(Contact)
        .filter(Contact.phone_number == contact.phone_number)
        .first()
    )

    if existing_phone:
        raise HTTPException(
            status_code=status.HTTP_409_CONFLICT,
            detail="This phone number already exists."
        )

    # Check duplicate email
    if contact.email:
        existing_email = (
            db.query(Contact)
            .filter(Contact.email == contact.email)
            .first()
        )

        if existing_email:
            raise HTTPException(
                status_code=status.HTTP_409_CONFLICT,
                detail="This email address already exists."
            )

    new_contact = Contact(
        name=contact.name,
        phone_number=contact.phone_number,
        email=contact.email,
        address=contact.address
    )

    try:
        db.add(new_contact)
        db.commit()
        db.refresh(new_contact)

        return new_contact

    except IntegrityError:
        db.rollback()

        raise HTTPException(
            status_code=status.HTTP_409_CONFLICT,
            detail="A contact with this phone number or email already exists."
        )


# GET ALL CONTACTS
@router.get(
    "/",
    response_model=list[ContactResponse]
)
def get_contacts(
    db: Session = Depends(get_db)
):

    contacts = (
        db.query(Contact)
        .order_by(Contact.name.asc())
        .all()
    )

    return contacts


# GET SINGLE CONTACT
@router.get(
    "/{contact_id}",
    response_model=ContactResponse
)
def get_contact(
    contact_id: int,
    db: Session = Depends(get_db)
):

    contact = (
        db.query(Contact)
        .filter(Contact.id == contact_id)
        .first()
    )

    if not contact:
        raise HTTPException(
            status_code=status.HTTP_404_NOT_FOUND,
            detail="Contact not found."
        )

    return contact


# UPDATE CONTACT
@router.put(
    "/{contact_id}",
    response_model=ContactResponse
)
def update_contact(
    contact_id: int,
    updated_contact: ContactUpdate,
    db: Session = Depends(get_db)
):

    contact = (
        db.query(Contact)
        .filter(Contact.id == contact_id)
        .first()
    )

    if not contact:
        raise HTTPException(
            status_code=status.HTTP_404_NOT_FOUND,
            detail="Contact not found."
        )

    # Only update fields that were actually sent
    update_data = updated_contact.model_dump(
        exclude_unset=True
    )

    # Check duplicate phone number
    if "phone_number" in update_data:

        existing_phone = (
            db.query(Contact)
            .filter(
                Contact.phone_number ==
                update_data["phone_number"],

                Contact.id != contact_id
            )
            .first()
        )

        if existing_phone:
            raise HTTPException(
                status_code=status.HTTP_409_CONFLICT,
                detail="This phone number already exists."
            )

    # Check duplicate email
    if (
        "email" in update_data
        and update_data["email"] is not None
    ):

        existing_email = (
            db.query(Contact)
            .filter(
                Contact.email ==
                update_data["email"],

                Contact.id != contact_id
            )
            .first()
        )

        if existing_email:
            raise HTTPException(
                status_code=status.HTTP_409_CONFLICT,
                detail="This email address already exists."
            )

    # Update fields
    for field, value in update_data.items():
        setattr(contact, field, value)

    try:
        db.commit()
        db.refresh(contact)

        return contact

    except IntegrityError:
        db.rollback()

        raise HTTPException(
            status_code=status.HTTP_409_CONFLICT,
            detail="A contact with this phone number or email already exists."
        )


# DELETE CONTACT
@router.delete(
    "/{contact_id}"
)
def delete_contact(
    contact_id: int,
    db: Session = Depends(get_db)
):

    contact = (
        db.query(Contact)
        .filter(Contact.id == contact_id)
        .first()
    )

    if not contact:
        raise HTTPException(
            status_code=status.HTTP_404_NOT_FOUND,
            detail="Contact not found."
        )

    db.delete(contact)
    db.commit()

    return {
        "message": "Contact deleted successfully."
    }