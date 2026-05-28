const monthInput = document.querySelector("#month");
const monthlyForm = document.querySelector("#monthly-form");
const stepOneForm = document.querySelector("#orders-step-one");
const stepTwoForm = document.querySelector("#orders-step-two");
const ordersInput = document.querySelector("#orders-input");
const ordersPreview = document.querySelector("#orders-preview");
const pastedCount = document.querySelector("#pasted-count");
const uniqueCount = document.querySelector("#unique-count");
const backButton = document.querySelector("#back-button");
const stepOneIndicator = document.querySelector("#step-one-indicator");
const stepTwoIndicator = document.querySelector("#step-two-indicator");
const toast = document.querySelector("#toast");
const currentUser = document.querySelector("#current-user");
const adminLink = document.querySelector("#admin-link");
const logoutButton = document.querySelector("#logout-button");

let normalizedOrders = [];

monthInput.value = new Date().toISOString().slice(0, 7);
loadCurrentUser();

monthlyForm.addEventListener("submit", (event) => {
  event.preventDefault();
  const month = monthInput.value;
  if (!month) {
    showToast("Selecciona un mes.");
    return;
  }

  window.location.href = `/reportes/mensual/download?month=${encodeURIComponent(month)}`;
});

stepOneForm.addEventListener("submit", (event) => {
  event.preventDefault();
  const tokens = splitOrders(ordersInput.value);
  normalizedOrders = distinct(tokens);

  if (normalizedOrders.length === 0) {
    showToast("Pega al menos un numero de pedido.");
    return;
  }

  pastedCount.textContent = tokens.length.toString();
  uniqueCount.textContent = normalizedOrders.length.toString();
  ordersPreview.value = normalizedOrders.join("\n");
  setStep(2);
});

stepTwoForm.addEventListener("submit", async (event) => {
  event.preventDefault();
  if (normalizedOrders.length === 0) {
    showToast("No hay pedidos para descargar.");
    return;
  }

  const submitButton = stepTwoForm.querySelector("button[type='submit']");
  submitButton.disabled = true;

  try {
    const response = await fetch("/reportes/pedidos/download", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ orderNumbers: normalizedOrders })
    });

    if (!response.ok) {
      throw new Error(await readError(response));
    }

    const blob = await response.blob();
    const fileName = getFileName(response.headers.get("content-disposition"));
    downloadBlob(blob, fileName);
  } catch (error) {
    showToast(error.message || "No se pudo generar el reporte.");
  } finally {
    submitButton.disabled = false;
  }
});

backButton.addEventListener("click", () => setStep(1));
logoutButton.addEventListener("click", async () => {
  await fetch("/auth/logout", { method: "POST" });
  window.location.href = "/login.html";
});

async function loadCurrentUser() {
  try {
    const response = await fetch("/auth/me");
    if (response.status === 401) {
      window.location.href = "/login.html";
      return;
    }

    if (response.status === 403) {
      window.location.href = "/force-password-change.html";
      return;
    }

    if (!response.ok) {
      return;
    }

    const user = await response.json();
    currentUser.textContent = user.fullName || user.email;
    adminLink.classList.toggle("hidden", !user.roles?.includes("Admin"));
  } catch {
    currentUser.textContent = "";
  }
}

function splitOrders(value) {
  return value
    .split(/[\s,;]+/g)
    .map((item) => item.trim())
    .filter(Boolean);
}

function distinct(values) {
  const seen = new Set();
  const result = [];

  for (const value of values) {
    if (seen.has(value)) {
      continue;
    }

    seen.add(value);
    result.push(value);
  }

  return result;
}

function setStep(step) {
  const showSecond = step === 2;
  stepOneForm.classList.toggle("hidden", showSecond);
  stepTwoForm.classList.toggle("hidden", !showSecond);
  stepOneIndicator.classList.toggle("active", !showSecond);
  stepTwoIndicator.classList.toggle("active", showSecond);
}

function downloadBlob(blob, fileName) {
  const url = URL.createObjectURL(blob);
  const link = document.createElement("a");
  link.href = url;
  link.download = fileName || "reporte-eventos-pedidos.xlsx";
  document.body.appendChild(link);
  link.click();
  link.remove();
  URL.revokeObjectURL(url);
}

function getFileName(contentDisposition) {
  if (!contentDisposition) {
    return null;
  }

  const utf8Match = contentDisposition.match(/filename\*=UTF-8''([^;]+)/i);
  if (utf8Match) {
    return decodeURIComponent(utf8Match[1]);
  }

  const asciiMatch = contentDisposition.match(/filename="?([^"]+)"?/i);
  return asciiMatch ? asciiMatch[1] : null;
}

async function readError(response) {
  if (response.status === 401) {
    window.location.href = "/login.html";
    return "Debes iniciar sesion.";
  }

  if (response.status === 403) {
    window.location.href = "/force-password-change.html";
    return "Debes cambiar tu contrasena antes de continuar.";
  }

  try {
    const body = await response.json();
    return body.detail || "No se pudo generar el reporte.";
  } catch {
    return "No se pudo generar el reporte.";
  }
}

function showToast(message) {
  toast.textContent = message;
  toast.classList.add("visible");
  window.clearTimeout(showToast.timeout);
  showToast.timeout = window.setTimeout(() => {
    toast.classList.remove("visible");
  }, 3200);
}
