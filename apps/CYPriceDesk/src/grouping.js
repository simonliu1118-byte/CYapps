// Pure grouping of one search listing against a tracked product (WO-1.3.0 §2.3 / §2.4).
// Titles are parsed with catalog-core.js parseTitle; this file only decides which group the result falls in.
import {parseTitle, normalize} from '../public/catalog-core.js';

// Unit aliases (卷→捲, PCS→入 ...) come from parseTitle, so a typed unit is normalised the same way.
export const normalizeUnit = unit => parseTitle(`1${unit}`).unit || normalize(unit);

const squash = value => normalize(value).replace(/\s+/g, '').toUpperCase();
const escapeRegExp = text => text.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');

// tracked: {model, attributes, packageQuantity, packageUnit}
// judgment (optional): {verdict, packageQuantity, packageUnit, titleSnapshot}
// Returns {group: same|other|pending|unrelated|excluded, quantity, unit, reason, parsed}.
export function classifyListing(tracked, title, judgment) {
  const parsed = parseTitle(title);
  const result = (group, reason = '', quantity = parsed.quantity, unit = parsed.unit) => ({group, quantity, unit, reason, parsed});

  if (judgment) {
    if (judgment.titleSnapshot !== title) return result('pending', '標題已變更，請重新確認');
    if (judgment.verdict === 'not_same') return result('excluded');
    if (judgment.verdict === 'same') return result('same', '人工確認', tracked.packageQuantity, tracked.packageUnit);
    return result('other', '人工確認', judgment.packageQuantity, judgment.packageUnit);
  }

  const model = squash(tracked.model);
  const found = squash(parsed.model);
  if (found) {
    // A listing whose model merely extends ours with a suffix (1530 / 1530-0) stays in the family; anything else is a different product.
    if (found !== model && !found.startsWith(model + '-')) return result('unrelated');
  } else if (!new RegExp(`(?<![A-Z0-9])${escapeRegExp(model)}(?![A-Z0-9])`).test(normalize(title).toUpperCase())) {
    return result('unrelated');
  }
  if (parsed.issues.length) return result('pending', parsed.issues.join('、'));
  if (!found) return result('pending', '無法辨識型號');
  if (!parsed.quantity || !parsed.unit) return result('pending', '無法辨識包裝數量');
  for (const [key, expected] of Object.entries(tracked.attributes || {})) {
    const actual = parsed.attributes[key];
    if (actual === undefined) return result('pending', `規格屬性「${key}」不明`);
    if (squash(actual) !== squash(expected)) return result('pending', `規格屬性「${key}」不同（標題為 ${actual}）`);
  }
  return parsed.quantity === tracked.packageQuantity && parsed.unit === tracked.packageUnit ? result('same') : result('other');
}
