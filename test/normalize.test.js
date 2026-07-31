const assert = require("node:assert/strict");
const { readFileSync } = require("node:fs");
const path = require("node:path");
const test = require("node:test");

const { normalize } = require("../src/normalize.js");

function readExample(filename) {
  return readFileSync(
    path.join(__dirname, "..", "examples", filename),
    "utf8",
  ).trimEnd();
}

test("removes a Markdown code fence and common indentation", () => {
  const input = readExample("markdown-fence.input.txt");
  const expected = readExample("markdown-fence.expected.txt");

  assert.equal(normalize(input), expected);
});

test("removes a dollar shell prompt", () => {
  const input = readExample("shell-prompt-dollar.input.txt");
  const expected = readExample("shell-prompt-dollar.expected.txt");

  assert.equal(normalize(input), expected);
});

test("removes an angle shell prompt", () => {
  const input = readExample("shell-prompt-angle.input.txt");
  const expected = readExample("shell-prompt-angle.expected.txt");

  assert.equal(normalize(input), expected);
});