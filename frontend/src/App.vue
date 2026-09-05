<script setup>
import { computed, onMounted, ref } from "vue";



const API_URL = "/api/contacts/";

const contacts = ref([]);
const loading = ref(true);
const errorMessage = ref("");

const searchQuery = ref("");
const visibleCount = ref(6);

const selectedContact = ref(null);

const showForm = ref(false);
const editingContact = ref(null);
const saving = ref(false);

const formError = ref("");

const form = ref({
  name: "",
  phone_number: "",
  email: "",
  address: ""
});

const validateName = (name) => {
  const nameRegex = /^[A-Za-zÀ-ÿ][A-Za-zÀ-ÿ\s'-]{1,99}$/;

  return nameRegex.test(name.trim());
};


const validatePhone = (phone) => {
  /*
    E.164-style international format

    Examples:
    +919876543210
    +14155552671
    +447911123456
  */

  const phoneRegex = /^\+[1-9]\d{7,14}$/;

  return phoneRegex.test(phone.trim());
};


const validateEmail = (email) => {
  const emailRegex =
    /^[^\s@]+@[^\s@]+\.[^\s@]{2,}$/;

  return emailRegex.test(email.trim());
};


const validateAddress = (address) => {
  const cleanedAddress = address.trim();

  /*
    Address rules:
    - At least 5 characters
    - Maximum 255 characters
    - Must contain at least one letter
    - Allows normal address characters
  */

  const addressRegex =
    /^(?=.*[A-Za-zÀ-ÿ])[A-Za-zÀ-ÿ0-9\s,.'#/-]{5,255}$/;

  return addressRegex.test(cleanedAddress);
};

/* -----------------------------
   FETCH CONTACTS
------------------------------ */

async function fetchContacts() {
  loading.value = true;
  errorMessage.value = "";

  try {
    const response = await fetch(API_URL);

    if (!response.ok) {
      throw new Error("Unable to load contacts");
    }

    contacts.value = await response.json();

  } catch (error) {
    errorMessage.value = error.message;
  } finally {
    loading.value = false;
  }
}


/* -----------------------------
   SORT + SEARCH
------------------------------ */

const filteredContacts = computed(() => {
  const query = searchQuery.value.toLowerCase().trim();

  return [...contacts.value]
    .sort((a, b) =>
      a.name.localeCompare(b.name)
    )
    .filter((contact) => {
      if (!query) return true;

      return (
        contact.name.toLowerCase().includes(query) ||
        contact.phone_number.includes(query) ||
        (contact.email &&
          contact.email.toLowerCase().includes(query))
      );
    });
});


const displayedContacts = computed(() => {
  return filteredContacts.value.slice(
    0,
    visibleCount.value
  );
});


const hasMoreContacts = computed(() => {
  return (
    visibleCount.value <
    filteredContacts.value.length
  );
});


function loadMore() {
  visibleCount.value += 6;
}


/* Reset pagination when searching */

function resetVisibleContacts() {
  visibleCount.value = 6;
}


/* -----------------------------
   CONTACT DETAILS
------------------------------ */

function openContact(contact) {
  selectedContact.value = contact;
}


function closeContact() {
  selectedContact.value = null;
}


/* -----------------------------
   FORM
------------------------------ */

function resetForm() {
  form.value = {
    name: "",
    phone_number: "",
    email: "",
    address: ""
  };

  formError.value = "";
}


function openCreateForm() {
  editingContact.value = null;

  resetForm();

  showForm.value = true;
}


function openEditForm(contact) {
  editingContact.value = contact;

  form.value = {
    name: contact.name,
    phone_number: contact.phone_number,
    email: contact.email || "",
    address: contact.address || ""
  };

  formError.value = "";

  showForm.value = true;

  closeContact();
}


function closeForm() {
  showForm.value = false;

  editingContact.value = null;

  resetForm();
}


/* -----------------------------
   CREATE / UPDATE
------------------------------ */

async function saveContact() {

  formError.value = "";

  const name = form.value.name.trim();
  const phone = form.value.phone_number.trim();
  const email = form.value.email.trim();
  const address = form.value.address.trim();


  /* NAME */

  if (!name) {
    formError.value =
      "Please enter a contact name.";

    return;
  }

  if (!validateName(name)) {
    formError.value =
      "Name must contain only letters, spaces, apostrophes or hyphens.";

    return;
  }


  /* PHONE */

  if (!phone) {
    formError.value =
      "Please enter a phone number.";

    return;
  }

  if (!validatePhone(phone)) {
    formError.value =
      "Enter a valid international phone number with a country code, for example +919876543210.";

    return;
  }


  /* EMAIL */

  if (email && !validateEmail(email)) {
    formError.value =
      "Please enter a valid email address, for example name@example.com.";

    return;
  }


  /* ADDRESS */

  if (address && !validateAddress(address)) {
    formError.value =
      "Please enter a valid address using letters, numbers and normal address characters.";

    return;
  }


  saving.value = true;


  try {

    const payload = {
      name,
      phone_number: phone,
      email: email || null,
      address: address || null
    };


    let response;


    /* UPDATE */

    if (editingContact.value) {

      response = await fetch(
        `${API_URL}${editingContact.value.id}`,
        {
          method: "PUT",

          headers: {
            "Content-Type": "application/json"
          },

          body: JSON.stringify(payload)
        }
      );

    }


    /* CREATE */

    else {

      response = await fetch(
        API_URL,
        {
          method: "POST",

          headers: {
            "Content-Type": "application/json"
          },

          body: JSON.stringify(payload)
        }
      );

    }


    if (!response.ok) {

      const errorData =
        await response.json().catch(() => null);


      let message =
        "Unable to save contact.";


      if (errorData?.detail) {

        if (Array.isArray(errorData.detail)) {

          message =
            errorData.detail
              .map((item) => item.msg)
              .join(", ");

        }

        else {

          message = errorData.detail;

        }

      }


      throw new Error(message);

    }


    await fetchContacts();

    closeForm();

  }


  catch (error) {

    formError.value =
      error.message ||
      "Something went wrong.";

  }


  finally {

    saving.value = false;

  }

}


/* -----------------------------
   DELETE
------------------------------ */

async function deleteContact(contact) {

  const confirmed =
    confirm(
      `Delete ${contact.name} from your phonebook?`
    );


  if (!confirmed) return;


  try {

    const response =
      await fetch(
        `${API_URL}${contact.id}`,
        {
          method: "DELETE"
        }
      );


    if (!response.ok) {
      throw new Error(
        "Unable to delete contact"
      );
    }


    closeContact();

    await fetchContacts();


  } catch (error) {

    alert(error.message);

  }

}


/* -----------------------------
   AVATAR INITIALS
------------------------------ */

function getInitials(name) {

  return name
    .split(" ")
    .map((word) => word[0])
    .join("")
    .slice(0, 2)
    .toUpperCase();

}


/* -----------------------------
   RESET PAGINATION
------------------------------ */

function handleSearch() {
  visibleCount.value = 6;
}


/* -----------------------------
   INITIAL LOAD
------------------------------ */

onMounted(() => {
  fetchContacts();
});
</script>


<template>

  <main class="app-shell">


    <!-- BACKGROUND DECORATION -->

    <div class="background-orb orb-one"></div>

    <div class="background-orb orb-two"></div>


    <!-- HEADER -->

    <header class="top-header">

      <div class="brand">

        <div class="brand-mark">

          <span></span>
          <span></span>
          <span></span>

        </div>

        <div>

          <p class="brand-label">
            PERSONAL CONTACT SPACE
          </p>

          <h1>
            Phonebook
          </h1>

        </div>

      </div>


      <button
        class="add-contact-button"
        @click="openCreateForm"
      >

        <span class="plus">
          +
        </span>

        Add contact

      </button>

    </header>



    <!-- HERO -->

    <section class="hero">

      <div>

        <p class="hero-kicker">
          YOUR PEOPLE, CONNECTED
        </p>

        <h2>
          Keep the important
          <span>
            people close.
          </span>
        </h2>

        <p class="hero-text">

          A simple place for every
          number, email and address
          that matters to you.

        </p>

      </div>


      <div class="network-visual">

        <div class="network-line line-one"></div>
        <div class="network-line line-two"></div>
        <div class="network-line line-three"></div>

        <div class="network-node node-main">
          {{ contacts.length }}
        </div>

        <div class="network-node node-one"></div>

        <div class="network-node node-two"></div>

        <div class="network-node node-three"></div>

        <span class="network-label">
          contacts in your network
        </span>

      </div>

    </section>



    <!-- SEARCH -->

    <section class="contacts-section">


      <div class="section-top">

        <div>

          <p class="section-label">
            YOUR PHONEBOOK
          </p>

          <h2>
            All contacts
          </h2>

        </div>


        <div class="contact-count">

          {{ filteredContacts.length }}

          <span>
            people
          </span>

        </div>

      </div>



      <div class="search-container">

        <span class="search-icon">
          ⌕
        </span>


        <input
          v-model="searchQuery"
          @input="handleSearch"
          type="text"
          placeholder="Search by name, phone number or email..."
        />


        <button
          v-if="searchQuery"
          class="clear-search"
          @click="searchQuery = ''; resetVisibleContacts()"
        >
          ×
        </button>

      </div>



      <!-- LOADING -->

      <div
        v-if="loading"
        class="loading-state"
      >

        <div class="loader"></div>

        <p>
          Gathering your contacts...
        </p>

      </div>



      <!-- ERROR -->

      <div
        v-else-if="errorMessage"
        class="error-state"
      >

        <h3>
          Something went wrong
        </h3>

        <p>
          {{ errorMessage }}
        </p>

        <button
          @click="fetchContacts"
        >
          Try again
        </button>

      </div>



      <!-- EMPTY -->

      <div
        v-else-if="
          filteredContacts.length === 0
        "
        class="empty-state"
      >

        <div class="empty-symbol">
          ✦
        </div>

        <h3>
          {{
            searchQuery
              ? "No matching contacts"
              : "Your phonebook is empty"
          }}
        </h3>

        <p>

          {{
            searchQuery
              ? "Try a different name or number."
              : "Start building your network by adding your first contact."
          }}

        </p>


        <button
          v-if="!searchQuery"
          @click="openCreateForm"
        >
          Add your first contact
        </button>

      </div>



      <!-- CONTACT CARDS -->

      <TransitionGroup
        v-else
        name="contact"
        tag="div"
        class="contacts-grid"
      >

        <article
          v-for="contact in displayedContacts"
          :key="contact.id"
          class="contact-card"
          @click="openContact(contact)"
        >

          <div class="card-top">

            <div class="avatar">
              {{ getInitials(contact.name) }}
            </div>


            <span class="card-arrow">
              ↗
            </span>

          </div>


          <div class="contact-info">

            <h3>
              {{ contact.name }}
            </h3>

            <p class="phone">
              {{ contact.phone_number }}
            </p>

          </div>


          <div class="contact-meta">

            <p
              v-if="contact.email"
            >
              <span>✉</span>

              {{ contact.email }}

            </p>


            <p
              v-if="contact.address"
            >
              <span>⌖</span>

              {{ contact.address }}

            </p>

          </div>


          <div class="card-footer">

            <span>
              View details
            </span>

            <span>
              →
            </span>

          </div>

        </article>

      </TransitionGroup>



      <!-- LOAD MORE -->

      <div
        v-if="
          !loading &&
          hasMoreContacts
        "
        class="load-more-wrapper"
      >

        <p>

          Showing

          {{ displayedContacts.length }}

          of

          {{ filteredContacts.length }}

          contacts

        </p>


        <button
          class="load-more-button"
          @click="loadMore"
        >

          See more

          <span>
            ↓
          </span>

        </button>

      </div>


    </section>



    <!-- CONTACT DETAIL PANEL -->

    <Transition name="panel">

      <aside
        v-if="selectedContact"
        class="detail-overlay"
        @click.self="closeContact"
      >

        <div class="detail-panel">


          <button
            class="panel-close"
            @click="closeContact"
          >
            ×
          </button>



          <div class="detail-avatar">

            {{
              getInitials(
                selectedContact.name
              )
            }}

          </div>


          <p class="detail-label">
            CONTACT DETAILS
          </p>


          <h2>
            {{ selectedContact.name }}
          </h2>



          <div class="detail-items">


            <div class="detail-item">

              <span>
                📞
              </span>

              <div>

                <small>
                  PHONE
                </small>

                <p>
                  {{
                    selectedContact.phone_number
                  }}
                </p>

              </div>

            </div>



            <div
              v-if="
                selectedContact.email
              "
              class="detail-item"
            >

              <span>
                ✉
              </span>

              <div>

                <small>
                  EMAIL
                </small>

                <p>
                  {{
                    selectedContact.email
                  }}
                </p>

              </div>

            </div>



            <div
              v-if="
                selectedContact.address
              "
              class="detail-item"
            >

              <span>
                📍
              </span>

              <div>

                <small>
                  ADDRESS
                </small>

                <p>
                  {{
                    selectedContact.address
                  }}
                </p>

              </div>

            </div>


          </div>



          <div class="detail-actions">

            <button
              class="detail-edit"
              @click="
                openEditForm(
                  selectedContact
                )
              "
            >

              Edit contact

            </button>


            <button
              class="detail-delete"
              @click="
                deleteContact(
                  selectedContact
                )
              "
            >

              Delete

            </button>

          </div>


        </div>

      </aside>

    </Transition>



    <!-- CREATE / EDIT MODAL -->

    <Transition name="modal">

      <div
        v-if="showForm"
        class="form-overlay"
        @click.self="closeForm"
      >


        <form
          class="contact-form"
          @submit.prevent="saveContact"
        >


          <div class="form-header">

            <div>

              <p>
                {{
                  editingContact
                    ? "UPDATE CONTACT"
                    : "NEW CONTACT"
                }}
              </p>


              <h2>

                {{
                  editingContact
                    ? "Edit contact"
                    : "Add someone new"
                }}

              </h2>

            </div>


            <button
              type="button"
              class="form-close"
              @click="closeForm"
            >
              ×
            </button>

          </div>



          <div
            v-if="formError"
            class="form-error"
          >
            {{ formError }}
          </div>



          <div class="form-fields">


<label>
  Full name *

  <input
    v-model="form.name"
    type="text"
    placeholder="e.g. Raj Sharma"
    maxlength="100"
    autocomplete="name"
  />

  <small class="input-hint">
    Use letters, spaces, apostrophes or hyphens.
  </small>
</label>



<label>
  Phone number *

  <input
    v-model="form.phone_number"
    type="tel"
    placeholder="+919876543210"
    inputmode="tel"
    autocomplete="tel"
  />

  <small class="input-hint">
    Include the country code. Example: +91 followed by your phone number.
  </small>
</label>



<label>
  Email

  <input
    v-model="form.email"
    type="email"
    placeholder="name@example.com"
    autocomplete="email"
  />

  <small class="input-hint">
    Optional. Enter a valid email address.
  </small>
</label>



<label>
  Address

  <textarea
    v-model="form.address"
    placeholder="e.g. Thane, Maharashtra, India"
    rows="3"
    maxlength="255"
    autocomplete="street-address"
  ></textarea>

  <small class="input-hint">
    Optional. Enter a city, full address or location.
  </small>
</label>


          </div>



          <div class="form-actions">

            <button
              type="button"
              class="cancel-button"
              @click="closeForm"
            >
              Cancel
            </button>


            <button
              type="submit"
              class="save-button"
              :disabled="saving"
            >

              {{
                saving
                  ? "Saving..."
                  : editingContact
                    ? "Save changes"
                    : "Add contact"
              }}

            </button>

          </div>


        </form>

      </div>

    </Transition>


  </main>

</template>


<style src="./App.css"></style>