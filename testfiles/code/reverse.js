const logger = require('pelias-logger').get('coarse_reverse');
const _ = require('lodash');
const Document = require('pelias-model').Document;
const Debug = require('../helper/debug');
const debugLog = new Debug('controller:coarse_reverse');

// do not change order, other functionality depends on most-to-least granular order
const coarse_granularities = [
  'neighbourhood',
  'borough',
  'locality',
  'localadmin',
  'county',
  'macrocounty',
  'region',
  'macroregion',
  'dependency',
  'country',
  'empire',
  'continent',
  'ocean',
  'marinearea'
];

// remove non-coarse layers and return what's left (or all if empty)
function getEffectiveLayers(requested_layers) {
  // remove non-coarse layers
  const non_coarse_layers_removed = _.without(requested_layers, 'venue', 'address', 'street');

  // if resulting array is empty, use all coarse granularities
  if (_.isEmpty(non_coarse_layers_removed)) {
    return coarse_granularities;
  }

  // otherwise use requested layers with non-coarse layers removed
  return non_coarse_layers_removed;

}

// drop from coarse_granularities until there's one that was requested
// this depends on coarse_granularities being ordered
function getApplicableRequestedLayers(requested_layers) {
  return _.dropWhile(coarse_granularities, (coarse_granularity) => {
    return !_.includes(requested_layers, coarse_granularity);
  });
}

//  removing non-coarse layers could leave effective_layers empty, so it's
//  important to check for empty layers here
function hasResultsAtRequestedLayers(results, requested_layers) {
  return !_.isEmpty(_.intersection(_.keys(results), requested_layers));
}
