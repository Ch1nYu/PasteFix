#!/usr/bin/env node

const { execFileSync } = require("node:child_process");
const { readFileSync } = require("node:fs");

const { normalize } = require("../src/normalize.js");

function getClipboard() {
  return execFileSync(
    "powershell.exe",
    ["-NoProfile", "-Command", "Get-Clipboard -Raw"],
    { encoding: "utf8" },
  );
}

function setClipboard(text) {
  execFileSync(
    "powershell.exe",
    ["-NoProfile", "-Command", "[Console]::In.ReadToEnd() | Set-Clipboard"],
    { input: text, encoding: "utf8" },
  );
}

const argument = process.argv[2];

if (argument === "--clipboard") {
  const cleanedText = normalize(getClipboard());
  setClipboard(cleanedText);
} else {
  const input = argument
    ? readFileSync(argument, "utf8")
    : readFileSync(0, "utf8");

  process.stdout.write(normalize(input));
}