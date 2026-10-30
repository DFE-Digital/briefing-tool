// Progress and button helpers, shared by the summary and the web search; they use only what they are given.

function setDisabled(button, isDisabled) {
   button.disabled = isDisabled;
   if (isDisabled) {
      button.setAttribute('aria-disabled', 'true');
   } else {
      button.removeAttribute('aria-disabled');
   }
}

// Each progress message is one step: the newest is in progress (spinner), every earlier one is done (tick).
function completeCurrentStep(list) {
   var current = list.querySelector('.app-briefing-status__step--current');
   if (!current) {
      return;
   }
   current.classList.remove('app-briefing-status__step--current');
   current.classList.add('app-briefing-status__step--done');
   current.querySelector('.govuk-visually-hidden').textContent = 'Done: ';
}

function stopCurrentStep(list) {
   var current = list.querySelector('.app-briefing-status__step--current');
   if (current) {
      current.classList.remove('app-briefing-status__step--current');
      current.querySelector('.govuk-visually-hidden').textContent = 'Stopped: ';
   }
}

function addStep(list, message) {
   completeCurrentStep(list);

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
   list.appendChild(item);
}

function startStatus(region, list) {
   list.innerHTML = '';
   region.hidden = false;
}

// The page's errors go in the layout's one error summary (_ErrorSummary.cshtml), marked up as its tag helper does it;
// an error about a field links to it.
function showErrorSummary(message, fieldId) {
   var container = document.getElementById('error-summary');
   container.innerHTML =
      '<div class="govuk-error-summary" data-module="govuk-error-summary" tabindex="-1"><div role="alert">' +
      '<h2 class="govuk-error-summary__title">There is a problem</h2>' +
      '<div class="govuk-error-summary__body"><ul class="govuk-list govuk-error-summary__list"><li></li></ul></div></div></div>';

   var item = container.querySelector('.govuk-error-summary__list li');
   if (fieldId) {
      var link = document.createElement('a');
      link.href = '#' + fieldId;
      link.textContent = message;
      item.append(link);
   } else {
      item.textContent = message;
   }
   container.querySelector('.govuk-error-summary').focus();
}

function clearErrorSummary() {
   document.getElementById('error-summary').innerHTML = '';
}

// Copies what an output shows, with its headings and lists, and as plain text for apps that only take text.
async function copyOutput(output, status, copiedMessage) {
   if (!output.querySelector('.briefing-output')) {
      return;
   }

   // innerText only follows the layout of an element on the page, so the copy is measured there, out of sight.
   var copied = output.cloneNode(true);
   copied.removeAttribute('id');
   copied.className = 'govuk-visually-hidden';
   document.body.append(copied);
   var text = copied.innerText;
   copied.remove();

   try {
      if (window.ClipboardItem && navigator.clipboard.write) {
         await navigator.clipboard.write([new ClipboardItem({
            'text/html': new Blob([copied.innerHTML], { type: 'text/html' }),
            'text/plain': new Blob([text], { type: 'text/plain' })
         })]);
      } else {
         await navigator.clipboard.writeText(text);
      }
      status.textContent = copiedMessage;
   } catch (error) {
      console.error(error);
      status.textContent = 'This could not be copied. Select the text and copy it instead.';
   }
}

export function initBriefingProgress() {
   var form = document.getElementById('briefing-form');
   if (!form || window.EventSource === undefined) {
      return;
   }

   var regenerateLink = document.getElementById('briefing-regenerate');
   var submitButton = document.getElementById('briefing-submit');
   var backLink = document.getElementById('briefing-back-link');
   var generateSection = document.getElementById('briefing-generate');
   var summarySection = document.getElementById('briefing-summary');
   var summaryTitle = document.getElementById('briefing-summary-title');
   var resultRegion = document.getElementById('briefing-result');
   var sourcesRegion = document.getElementById('briefing-sources');
   var statusRegion = document.getElementById('briefing-status');
   var statusList = document.getElementById('briefing-status-list');
   var outputContainer = document.getElementById('briefing-output-container');
   var copyRegion = document.getElementById('briefing-copy');
   var copyButton = document.getElementById('briefing-copy-button');
   var copyStatus = document.getElementById('briefing-copy-status');
   var webSearchOffer = document.getElementById('web-search-offer');
   var webSearchStatus = document.getElementById('web-search-status');
   var webSearchStatusList = document.getElementById('web-search-status-list');
   var webSearchResult = document.getElementById('web-search-result');
   var webSearchOutput = document.getElementById('web-search-output');
   var webSearchCopy = document.getElementById('web-search-copy');
   var webSearchCopyButton = document.getElementById('web-search-copy-button');
   var webSearchCopyStatus = document.getElementById('web-search-copy-status');
   var webSearchTitle = document.getElementById('web-search-title');
   var webSearchSources = document.getElementById('web-search-sources');
   var disclaimer = document.getElementById('AcceptDisclaimer');
   var disclaimerGroup = document.getElementById('briefing-disclaimer-group');
   var disclaimerFieldset = disclaimerGroup.querySelector('fieldset');
   var disclaimerMessage = 'Confirm you understand you need to apply professional judgement when using content generated by AI';
   // The type chosen on Briefing/Type, passed on so the server looks for that type, and the school's URN or trust's TRN.
   var type = form.querySelector('[name="Type"]').value;
   var urnOrTrn = form.querySelector('[name="UrnOrTrn"]').value;

   // The briefing the web search uses as evidence, as the server sent it.
   var pendingWebSearch = null;
   var isGenerating = false;

   // Generate is only disabled while a summary is generating, so it can't be run twice at once. It isn't greyed out
   // until the disclaimer is ticked (GOV.UK advises against disabled buttons): without the tick it says why instead.
   function updateSubmitButton() {
      setDisabled(submitButton, isGenerating);
   }

   function hideWebSearchOffer() {
      pendingWebSearch = null;
      webSearchOffer.hidden = true;
   }

   // Before a summary: the disclaimer and Generate. While generating and after: the summary.
   function showGenerate() {
      summarySection.hidden = true;
      generateSection.hidden = false;
      backLink.hidden = false;
   }

   function showSummary() {
      generateSection.hidden = true;
      backLink.hidden = true;
      summarySection.hidden = false;
   }

   function showResult(isShown) {
      resultRegion.hidden = !isShown;
      sourcesRegion.hidden = !isShown;
   }

   function clearWebSearch() {
      webSearchOutput.innerHTML = '';
      webSearchCopyStatus.textContent = '';
      webSearchResult.hidden = true;
      webSearchSources.hidden = true;
   }

   function clearBriefing() {
      outputContainer.innerHTML = '';
      copyStatus.textContent = '';
      webSearchStatus.hidden = true;
      clearWebSearch();
      hideWebSearchOffer();
      showResult(false);
   }

   // The briefing goes in the blue panel between the two AI warnings, both already on the page.
   function renderBriefing(html) {
      var output = document.createElement('div');
      output.className = 'briefing-output';
      output.innerHTML = html;
      outputContainer.replaceChildren(output);
      copyRegion.hidden = false;
      showResult(true);
   }

   // The web search findings go in their own Further context section under the summary: take the reader to them.
   function renderWebSearchFindings(html) {
      var findings = document.createElement('div');
      findings.className = 'briefing-output';
      findings.innerHTML = html;
      webSearchOutput.replaceChildren(findings);
      webSearchCopy.hidden = false;
      webSearchResult.hidden = false;
      webSearchSources.hidden = false;

      webSearchTitle.focus();
      webSearchTitle.scrollIntoView({ block: 'start' });
   }

   // Each section copies only its own output: the summary, or its further context.
   copyButton.addEventListener('click', function () {
      copyOutput(outputContainer, copyStatus, 'Summary copied');
   });

   webSearchCopyButton.addEventListener('click', function () {
      copyOutput(webSearchOutput, webSearchCopyStatus, 'Further context copied');
   });

   // Copying needs JavaScript, so a summary or further context already on the page gets its copy link now.
   copyRegion.hidden = !outputContainer.querySelector('.briefing-output');
   webSearchCopy.hidden = !webSearchOutput.querySelector('.briefing-output');

   // Without a briefing streamed in here, the offer's form sends back the one the page was given, as without JavaScript.
   webSearchOffer.addEventListener('submit', async function (event) {
      var request = pendingWebSearch;
      if (!request) {
         return;
      }
      event.preventDefault();

      hideWebSearchOffer();
      clearErrorSummary();
      startStatus(webSearchStatus, webSearchStatusList);
      addStep(webSearchStatusList, 'Searching local and national news for ' + request.schoolOrTrustName + '...');

      function showWebSearchError(message) {
         stopCurrentStep(webSearchStatusList);
         webSearchStatus.hidden = true;
         showErrorSummary(message);
         pendingWebSearch = request;
         webSearchOffer.hidden = false;
      }

      try {
         var token = form.querySelector('input[name="__RequestVerificationToken"]');
         var response = await fetch(window.location.pathname + '?' + new URLSearchParams({ handler: 'WebSearch', type: type }).toString(), {
            method: 'POST',
            headers: {
               'Content-Type': 'application/json',
               'RequestVerificationToken': token ? token.value : ''
            },
            body: JSON.stringify(request)
         });
         var result = await response.json().catch(function () { return {}; });

         if (!response.ok || typeof result.html !== 'string') {
            showWebSearchError(result.error || 'Sorry, something went wrong searching the web. Your summary is unchanged - please try again.');
            return;
         }

         completeCurrentStep(webSearchStatusList);
         webSearchStatus.hidden = true;
         renderWebSearchFindings(result.html);
      } catch (error) {
         // A network failure: the summary on the page is untouched, so the person can simply try again.
         console.error(error);
         showWebSearchError('Sorry, something went wrong searching the web. Your summary is unchanged - please try again.');
      }
   });

   // Clear any stale validation messages from a previous attempt, so that the user can see the new ones - including
   // the server's: the error summary and the checkbox's error message.
   function clearStaleValidation() {
      clearErrorSummary();
      disclaimerGroup.classList.remove('govuk-form-group--error');
      document.getElementById('AcceptDisclaimer-error')?.remove();
      disclaimerFieldset.removeAttribute('aria-describedby');
   }

   // The disclaimer must be accepted before a summary is generated - the same check the server makes without
   // JavaScript. The error is marked up as the tag helper does it.
   function validateDisclaimer() {
      if (disclaimer.checked) {
         return true;
      }

      var error = document.createElement('p');
      error.id = 'AcceptDisclaimer-error';
      error.className = 'govuk-error-message';
      error.innerHTML = '<span class="govuk-visually-hidden">Error:</span> ';
      error.append(disclaimerMessage);
      disclaimerFieldset.querySelector('.govuk-checkboxes').before(error);
      disclaimerGroup.classList.add('govuk-form-group--error');
      disclaimerFieldset.setAttribute('aria-describedby', 'AcceptDisclaimer-error');

      showErrorSummary(disclaimerMessage, 'AcceptDisclaimer');
      return false;
   }

   // Generates the summary, streaming its progress in; Re-generate runs the same again.
   function generate() {
      if (isGenerating) {
         return;
      }

      isGenerating = true;
      updateSubmitButton();
      clearStaleValidation();
      clearBriefing();
      showSummary();
      startStatus(statusRegion, statusList);
      summaryTitle.focus();

      var query = new URLSearchParams({ handler: 'Stream', type: type, urnOrTrn: urnOrTrn });
      var source = new EventSource(window.location.pathname + '?' + query.toString());
      var offer = null;

      function finish() {
         source.close();
         isGenerating = false;
         updateSubmitButton();
      }

      // The summary failed: back to the disclaimer, so the person can try again.
      function showError(message) {
         stopCurrentStep(statusList);
         statusRegion.hidden = true;
         showGenerate();
         showErrorSummary(message);
         finish();
      }

      source.addEventListener('progress', function (e) {
         addStep(statusList, e.data);
      });

      // Arrives just before "complete": the briefing to send back if the person asks for a web search.
      source.addEventListener('web-search-offer', function (e) {
         offer = JSON.parse(e.data);
      });

      source.addEventListener('complete', function (e) {
         completeCurrentStep(statusList);
         statusRegion.hidden = true;
         renderBriefing(e.data);
         finish();

         // The offer sits below the summary, so start the reader at the summary, not the offer.
         if (offer) {
            pendingWebSearch = offer;
            webSearchOffer.hidden = false;
         }
         summaryTitle.focus();
      });

      source.addEventListener('error-message', function (e) {
         showError(e.data);
      });

      source.onerror = function () {
         if (source.readyState === EventSource.CLOSED) {
            return;
         }
         showError('Sorry, something went wrong generating this summary. Please try again.');
      };
   }

   form.addEventListener('submit', function (event) {
      event.preventDefault();

      clearStaleValidation();
      if (validateDisclaimer()) {
         generate();
      }
   });

   // The link goes back to the disclaimer without JavaScript; with it, the summary is generated again straight away.
   regenerateLink.addEventListener('click', function (event) {
      event.preventDefault();
      generate();
   });
}
