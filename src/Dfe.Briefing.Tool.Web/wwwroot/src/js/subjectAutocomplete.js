// The Find page's search for a school or trust, as an accessible autocomplete (the GOV.UK pattern, loaded by the
// layout as window.accessibleAutocomplete). Matches come from the page's Search handler as the person types; a chosen
// match reads "Name (URN 123456)", and the page looks up the reference at its end. Without JavaScript, or if a search
// fails, the person can still enter the name, URN or TRN and continue.

// Wait this long after the last key press before searching, so typing a name isn't a search per letter.
var SEARCH_DELAY_MS = 250;

function escapeHtml(text) {
   var element = document.createElement('span');
   element.textContent = text;
   return element.innerHTML;
}

// A suggestion is a match from the server, or the text the person typed or came back with.
function suggestionText(suggestion) {
   if (!suggestion) {
      return '';
   }
   return typeof suggestion === 'string' ? suggestion : suggestion.text;
}

function suggestionHtml(suggestion) {
   if (typeof suggestion === 'string') {
      return escapeHtml(suggestion);
   }
   var region = suggestion.region ? '<span class="app-autocomplete__hint">' + escapeHtml(suggestion.region) + '</span>' : '';
   return escapeHtml(suggestion.text) + region;
}

export function initSubjectAutocomplete() {
   var container = document.getElementById('subject-autocomplete');
   if (!container || !window.accessibleAutocomplete || !window.fetch) {
      return;
   }

   var input = container.querySelector('#UrnOrTrn');
   var searchUrl = container.getAttribute('data-search-url');
   var minimumLength = Number(container.getAttribute('data-minimum-length'));
   var subject = container.getAttribute('data-subject');

   var timer = null;
   var request = null;
   var isSearching = false;

   // Each key press cancels the search before it, so only the latest text's matches are ever shown.
   function search(query, populateResults) {
      clearTimeout(timer);
      request?.abort();

      timer = setTimeout(async function () {
         request = new AbortController();
         isSearching = true;
         try {
            var response = await fetch(searchUrl + '&query=' + encodeURIComponent(query), { signal: request.signal });
            populateResults(response.ok ? await response.json() : []);
         } catch (error) {
            if (error.name !== 'AbortError') {
               console.error(error);
               populateResults([]);
            }
         } finally {
            isSearching = false;
         }
      }, SEARCH_DELAY_MS);
   }

   // The autocomplete renders its own input in place of the page's, with the same id and name, so the label, the
   // error summary's link and the submitted value all still work.
   var wrapper = document.createElement('div');
   wrapper.className = 'app-autocomplete';
   input.replaceWith(wrapper);

   window.accessibleAutocomplete({
      element: wrapper,
      id: 'UrnOrTrn',
      name: 'UrnOrTrn',
      defaultValue: input.value,
      minLength: minimumLength,
      // Typed text that isn't a match is kept, so a URN, TRN or whole name can still be entered.
      confirmOnBlur: false,
      showNoOptionsFound: true,
      tNoResults: function () {
         return isSearching ? 'Searching...' : 'No ' + subject + ' found';
      },
      templates: {
         inputValue: suggestionText,
         suggestion: suggestionHtml
      },
      source: search
   });

   // Keep the hint and any error read out with the field, as the page's own input had them.
   var describedBy = input.getAttribute('aria-describedby');
   var autocompleteInput = wrapper.querySelector('input');
   if (describedBy && autocompleteInput) {
      autocompleteInput.setAttribute('aria-describedby',
         [autocompleteInput.getAttribute('aria-describedby'), describedBy].filter(Boolean).join(' '));
   }
}
