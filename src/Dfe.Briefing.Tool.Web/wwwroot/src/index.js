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
   var statusTitle = document.getElementById('briefing-status-title');
   var webSearchOffer = document.getElementById('web-search-offer');
   var webSearchYes = document.getElementById('web-search-yes');
   var webSearchNo = document.getElementById('web-search-no');

   // The briefing the person can choose to add web search findings to, as the server sent it.
   var pendingWebSearch = null;

   function hideWebSearchOffer() {
      pendingWebSearch = null;
      webSearchOffer.hidden = true;
   }

   clearButton.addEventListener('click', function () {
      outputContainer.innerHTML = '';
      clearButton.hidden = true;
      hideWebSearchOffer();
      form.querySelector('[name="Input.SchoolUrnOrTrustTrn"]')?.focus();
   });

   // Each progress message is one step: the newest is in progress (spinner), every earlier one is done (tick).
   function completeCurrentStep() {
      var current = statusList.querySelector('.app-briefing-status__step--current');
      if (!current) {
         return;
      }
      current.classList.remove('app-briefing-status__step--current');
      current.classList.add('app-briefing-status__step--done');
      current.querySelector('.govuk-visually-hidden').textContent = 'Done: ';
   }

   function stopCurrentStep() {
      var current = statusList.querySelector('.app-briefing-status__step--current');
      if (current) {
         current.classList.remove('app-briefing-status__step--current');
         current.querySelector('.govuk-visually-hidden').textContent = 'Stopped: ';
      }
   }

   function addStep(message) {
      completeCurrentStep();

      var item = document.createElement('li');
      item.className = 'app-briefing-status__step app-briefing-status__step--current';

      var icon = document.createElement('span');
      icon.className = 'app-briefing-status__icon';
      icon.setAttribute('aria-hidden', 'true');

      var state = document.createElement('span');
      state.className = 'govuk-visually-hidden';
      state.textContent = 'In progress: ';

      var text = document.createElement('span');
      text.textContent = message;

      item.append(icon, state, text);
      statusList.appendChild(item);
   }

   function startStatus(title) {
      statusTitle.textContent = title;
      statusList.innerHTML = '';
      statusRegion.hidden = false;
   }

   function showErrorSummary(message) {
      errorSummary.innerHTML =
         '<div class="govuk-error-summary" data-module="govuk-error-summary" aria-labelledby="briefing-error-title" role="alert" tabindex="-1">' +
         '<h2 class="govuk-error-summary__title" id="briefing-error-title">There is a problem</h2>' +
         '<div class="govuk-error-summary__body"><ul class="govuk-list govuk-error-summary__list"><li></li></ul></div></div>';
      errorSummary.querySelector('.govuk-error-summary__list li').textContent = message;
      errorSummary.querySelector('.govuk-error-summary').focus();
      stopCurrentStep();
   }

   function renderBriefing(html) {
      outputContainer.innerHTML = '';
      var output = document.createElement('div');
      output.className = 'briefing-output';
      output.innerHTML = html;
      outputContainer.appendChild(output);
      var warning = document.getElementById('briefing-check-warning');
      if (warning) {
         outputContainer.appendChild(warning.content.cloneNode(true));
      }
   }

   // The web search findings come in a new More background information section at the end: take the reader to it.
   function focusNewsSection() {
      var headings = outputContainer.querySelectorAll('.briefing-output h2');
      var news = Array.prototype.find.call(headings, function (heading) {
         return heading.textContent.trim() === 'More background information';
      });
      if (news) {
         news.setAttribute('tabindex', '-1');
         news.focus();
         news.scrollIntoView({ block: 'start' });
      }
   }

   webSearchNo.addEventListener('click', function () {
      hideWebSearchOffer();
      outputContainer.querySelector('.briefing-output h1, .briefing-output h2')?.setAttribute('tabindex', '-1');
      outputContainer.querySelector('.briefing-output h1, .briefing-output h2')?.focus();
   });

   webSearchYes.addEventListener('click', async function () {
      var request = pendingWebSearch;
      if (!request) {
         return;
      }

      hideWebSearchOffer();
      errorSummary.innerHTML = '';
      submitButton.disabled = true;
      submitButton.setAttribute('aria-disabled', 'true');
      startStatus('Searching the web...');
      addStep('Searching local and national news for ' + request.schoolOrTrustName + '...');

      try {
         var token = form.querySelector('input[name="__RequestVerificationToken"]');
         var response = await fetch(window.location.pathname + '?handler=WebSearch', {
            method: 'POST',
            headers: {
               'Content-Type': 'application/json',
               'RequestVerificationToken': token ? token.value : ''
            },
            body: JSON.stringify(request)
         });
         var result = await response.json().catch(function () { return {}; });

         if (!response.ok || typeof result.html !== 'string') {
            showErrorSummary(result.error || 'Sorry, something went wrong searching the web. Your briefing is unchanged - please try again.');
            pendingWebSearch = request;
            webSearchOffer.hidden = false;
            return;
         }

         completeCurrentStep();
         renderBriefing(result.html);
         statusRegion.hidden = true;
         focusNewsSection();
      } catch (error) {
         // A network failure: the briefing on the page is untouched, so the person can simply try again.
         console.error(error);
         showErrorSummary('Sorry, something went wrong searching the web. Your briefing is unchanged - please try again.');
         pendingWebSearch = request;
         webSearchOffer.hidden = false;
      } finally {
         submitButton.disabled = false;
         submitButton.removeAttribute('aria-disabled');
      }
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
      hideWebSearchOffer();
      startStatus('Generating briefing...');
      submitButton.disabled = true;
      submitButton.setAttribute('aria-disabled', 'true');

      var query = new URLSearchParams({
         handler: 'Stream',
         schoolUrnOrTrustTrn: urnField.value.trim(),
         schoolOrTrustName: nameField.value.trim()
      });

      var source = new EventSource(window.location.pathname + '?' + query.toString());
      var offer = null;

      function finish() {
         source.close();
         submitButton.disabled = false;
         submitButton.removeAttribute('aria-disabled');
      }

      function showError(message) {
         showErrorSummary(message);
         finish();
      }

      source.addEventListener('progress', function (e) {
         addStep(e.data);
      });

      // Arrives just before "complete": the briefing to send back if the person asks for a web search.
      source.addEventListener('web-search-offer', function (e) {
         offer = JSON.parse(e.data);
      });

      source.addEventListener('complete', function (e) {
         renderBriefing(e.data);
         clearButton.hidden = false;
         statusRegion.hidden = true;
         finish();

         if (offer) {
            pendingWebSearch = offer;
            webSearchOffer.hidden = false;
            document.getElementById('web-search-offer-title').focus();
         }
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