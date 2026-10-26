import { evaluateRegex } from '../utils/regex.js';
self.onmessage = ({ data }) => {
    try { self.postMessage(evaluateRegex(data)); }
    catch (error) { self.postMessage({ error: error.message }); }
};
