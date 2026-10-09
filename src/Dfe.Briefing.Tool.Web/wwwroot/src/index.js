// Please see documentation at https://docs.microsoft.com/aspnet/core/client-side/bundling-and-minification
// for details on configuring this project to bundle and minify static web assets.

// Write your JavaScript code.
import * as GOVUKFrontend from "govuk-frontend";
import * as MOJFrontend from "@ministryofjustice/frontend";

window.GOVUKFrontend = GOVUKFrontend;
window.MOJFrontend = MOJFrontend;

// Initialize everything when DOM is ready
document.addEventListener('DOMContentLoaded', function () {

   // Initialize GOV.UK Frontend
   GOVUKFrontend.initAll();

   // Initialize MOJ Frontend
   MOJFrontend.initAll();

   initBriefingProgress();
});

function initBriefingProgress() {
   var form = document.getElementById('briefing-form');
   if (!form || typeof window.EventSource === 'undefined') {
      return;
   }

   var submitButton = document.getElementById('briefing-submit');
   var clearButton = document.getElementById('briefing-clear');
   var statusRegion = document.getElementById('briefing-status');
   var statusList = document.getElementById('briefing-status-list');
   var outputContainer = document.getElementById('briefing-output-container');
   var errorSummary = document.getElementById('briefing-error-summary');
   var validationSummary = document.getElementById('briefing-validation-summary');

   clearButton.addEventListener('click', function () {
      outputContainer.innerHTML = '';
      clearButton.hidden = true;
      form.querySelector('[name="Input.SchoolUrnOrTrustTrn"]')?.focus();
   });

    // Clear any stale validation messages from a previous submission attempt, so that the user can see the new ones.
   function clearStaleValidation() {
      errorSummary.innerHTML = '';
      validationSummary.innerHTML = '';

      form.querySelectorAll('[data-valmsg-for]').forEach(function (span) {
         span.textContent = '';
      });
      form.querySelectorAll('.govuk-form-group--error').forEach(function (group) {
         group.classList.remove('govuk-form-group--error');
      });
      form.querySelectorAll('.govuk-input--error').forEach(function (input) {
         input.classList.remove('govuk-input--error');
      });
   }

    // Validate the form fields and display any validation errors.
   function validate(fields) {
      var errors = fields.filter(function (field) {
         return !field.field.value.trim();
      });

      errors.forEach(function (error) {
         var group = error.field.closest('.govuk-form-group');
         if (group) {
            group.classList.add('govuk-form-group--error');
         }
         error.field.classList.add('govuk-input--error');
         var messageSpan = form.querySelector('[data-valmsg-for="' + error.field.name + '"]');
         if (messageSpan) {
            messageSpan.textContent = error.message;
         }
      });

      if (errors.length === 0) {
         return true;
      }

      validationSummary.innerHTML =
         '<div class="govuk-error-summary" aria-labelledby="error-summary-title" role="alert" tabindex="-1" data-module="govuk-error-summary">' +
         '<h2 class="govuk-error-summary__title" id="error-summary-title">There is a problem</h2>' +
         '<div class="govuk-error-summary__body"><ul class="govuk-list govuk-error-summary__list"></ul></div></div>';
      var list = validationSummary.querySelector('.govuk-error-summary__list');
      errors.forEach(function (error) {
         var item = document.createElement('li');
         var link = document.createElement('a');
         link.href = '#' + error.field.id;
         link.textContent = error.message;
         item.appendChild(link);
         list.appendChild(item);
      });
      validationSummary.querySelector('.govuk-error-summary').focus();

      return false;
   }

   form.addEventListener('submit', function (event) {
      event.preventDefault();

      var urnField = form.querySelector('[name="Input.SchoolUrnOrTrustTrn"]');
      var nameField = form.querySelector('[name="Input.SchoolOrTrustName"]');
      var webSearchField = form.querySelector('[name="Input.AllowWebSearch"]');

      clearStaleValidation();

      var isValid = validate([
         { field: urnField, message: 'Enter the school URN or trust TRN' },
         { field: nameField, message: 'Enter the school or trust name' }
      ]);
      if (!isValid) {
         return;
      }

      outputContainer.innerHTML = '';
      clearButton.hidden = true;
      statusList.innerHTML = '';
      statusRegion.hidden = false;
      submitButton.disabled = true;
      submitButton.setAttribute('aria-disabled', 'true');

      var query = new URLSearchParams({
         handler: 'Stream',
         schoolUrnOrTrustTrn: urnField.value.trim(),
         schoolOrTrustName: nameField.value.trim(),
         allowWebSearch: webSearchField && webSearchField.checked ? 'true' : 'false'
      });

      var source = new EventSource(window.location.pathname + '?' + query.toString());

      function finish() {
         source.close();
         submitButton.disabled = false;
         submitButton.removeAttribute('aria-disabled');
      }

      function showError(message) {
         errorSummary.innerHTML =
            '<div class="govuk-error-summary" data-module="govuk-error-summary" aria-labelledby="briefing-error-title" role="alert" tabindex="-1">' +
            '<h2 class="govuk-error-summary__title" id="briefing-error-title">There is a problem</h2>' +
            '<div class="govuk-error-summary__body"><ul class="govuk-list govuk-error-summary__list"><li></li></ul></div></div>';
         errorSummary.querySelector('.govuk-error-summary__list li').textContent = message;
         errorSummary.querySelector('.govuk-error-summary').focus();
         finish();
      }

      source.addEventListener('progress', function (e) {
         var item = document.createElement('li');
         item.textContent = e.data;
         statusList.appendChild(item);
      });

      source.addEventListener('complete', function (e) {
         var output = document.createElement('div');
         output.className = 'briefing-output';
         output.innerHTML = e.data;
         outputContainer.appendChild(output);
         clearButton.hidden = false;
         statusRegion.hidden = true;
         finish();
      });

      source.addEventListener('error-message', function (e) {
         showError(e.data);
      });

      source.onerror = function () {
         if (source.readyState === EventSource.CLOSED) {
            return;
         }
         showError('Sorry, something went wrong generating this briefing. Please try again.');
      };
   });
}