/**
 * OpenAPI helpers for the API Explorer.
 *
 * These flatten a fetched OpenAPI document into a list of operations and derive
 * sensible request defaults + code snippets. Kept dependency-free.
 */

const METHODS = ['get', 'post', 'put', 'patch', 'delete', 'head', 'options'];

/**
 * Flatten an OpenAPI spec into a flat list of operations.
 * Each operation: { id, tag, method, path, summary, parameters, requestBody, responses }
 */
export function flattenOpenApiSpec(spec) {
  if (!spec || !spec.paths) return [];
  const operations = [];
  for (const [path, pathItem] of Object.entries(spec.paths)) {
    const commonParams = pathItem.parameters || [];
    for (const method of METHODS) {
      const op = pathItem[method];
      if (!op) continue;
      const tag = (op.tags && op.tags[0]) || 'Default';
      operations.push({
        id: op.operationId || `${method.toUpperCase()} ${path}`,
        tag,
        method: method.toUpperCase(),
        path,
        summary: op.summary || op.description || '',
        parameters: [...commonParams, ...(op.parameters || [])],
        requestBody: op.requestBody || null,
        responses: op.responses || {}
      });
    }
  }
  return operations;
}

/** Group flattened operations by tag for the operation picker. */
export function groupByTag(operations) {
  const groups = {};
  for (const op of operations) {
    if (!groups[op.tag]) groups[op.tag] = [];
    groups[op.tag].push(op);
  }
  return Object.entries(groups)
    .map(([tag, ops]) => ({ tag, operations: ops }))
    .sort((a, b) => a.tag.localeCompare(b.tag));
}

/** Resolve a $ref against the spec's components. */
function resolveRef(ref, spec) {
  if (!ref || !ref.startsWith('#/')) return null;
  const parts = ref.slice(2).split('/');
  let node = spec;
  for (const part of parts) {
    node = node?.[part];
    if (node === undefined) return null;
  }
  return node;
}

/** Given a parameter, return a sensible default value. */
export function getParameterDefault(parameter) {
  const schema = parameter.schema || {};
  if (parameter.example !== undefined) return parameter.example;
  if (schema.example !== undefined) return schema.example;
  if (schema.default !== undefined) return schema.default;
  if (Array.isArray(schema.enum) && schema.enum.length > 0) return schema.enum[0];
  switch (schema.type) {
    case 'integer':
    case 'number':
      return '';
    case 'boolean':
      return 'false';
    default:
      return '';
  }
}

function schemaExample(schema, spec, depth = 0) {
  if (!schema || depth > 6) return null;
  if (schema.$ref) return schemaExample(resolveRef(schema.$ref, spec), spec, depth + 1);
  if (schema.example !== undefined) return schema.example;
  if (Array.isArray(schema.allOf)) {
    return schema.allOf.reduce((acc, s) => ({ ...acc, ...(schemaExample(s, spec, depth + 1) || {}) }), {});
  }
  if (schema.enum) return schema.enum[0];
  switch (schema.type) {
    case 'object': {
      const obj = {};
      const props = schema.properties || {};
      for (const [key, propSchema] of Object.entries(props)) {
        obj[key] = schemaExample(propSchema, spec, depth + 1);
      }
      return obj;
    }
    case 'array':
      return [schemaExample(schema.items, spec, depth + 1)].filter((v) => v !== null);
    case 'integer':
    case 'number':
      return schema.default ?? 0;
    case 'boolean':
      return schema.default ?? false;
    case 'string':
      return schema.default ?? '';
    default:
      return null;
  }
}

/** Given a requestBody, return an example JSON body string. */
export function getRequestBodyTemplate(requestBody, spec) {
  if (!requestBody) return '';
  let rb = requestBody;
  if (rb.$ref) rb = resolveRef(rb.$ref, spec);
  const content = rb?.content || {};
  const json = content['application/json'];
  if (!json || !json.schema) return '';
  const example = json.example || schemaExample(json.schema, spec);
  if (example === null || example === undefined) return '';
  return JSON.stringify(example, null, 2);
}

/** Build curl, fetch, and C# HttpClient code snippets for a composed request. */
export function buildCodeSnippets({ method, url, headers = {}, body }) {
  const headerEntries = Object.entries(headers).filter(([k]) => k);

  const curlParts = [`curl -X ${method} '${url}'`];
  for (const [k, v] of headerEntries) curlParts.push(`  -H '${k}: ${v}'`);
  if (body) curlParts.push(`  -d '${body.replace(/'/g, "'\\''")}'`);
  const curl = curlParts.join(' \\\n');

  const fetchHeaders = JSON.stringify(Object.fromEntries(headerEntries), null, 2);
  const fetchSnippet =
    `await fetch('${url}', {\n` +
    `  method: '${method}',\n` +
    `  headers: ${fetchHeaders}` +
    (body ? `,\n  body: ${JSON.stringify(body)}` : '') +
    `\n});`;

  const csLines = [
    'using var client = new HttpClient();',
    `var request = new HttpRequestMessage(HttpMethod.${method.charAt(0) + method.slice(1).toLowerCase()}, "${url}");`
  ];
  for (const [k, v] of headerEntries) {
    if (k.toLowerCase() === 'content-type') continue;
    csLines.push(`request.Headers.Add("${k}", "${v}");`);
  }
  if (body) {
    csLines.push(`request.Content = new StringContent(${JSON.stringify(body)}, System.Text.Encoding.UTF8, "application/json");`);
  }
  csLines.push('var response = await client.SendAsync(request);');
  csLines.push('var responseBody = await response.Content.ReadAsStringAsync();');

  return { curl, fetch: fetchSnippet, csharp: csLines.join('\n') };
}

/** Substitute {param} placeholders in a path with provided path params. */
export function substitutePathParams(path, pathParams = {}) {
  return path.replace(/\{([^}]+)\}/g, (_, name) => {
    const value = pathParams[name];
    return value !== undefined && value !== '' ? encodeURIComponent(value) : `{${name}}`;
  });
}
