#!/usr/bin/env node

const { readFileSync } = require("node:fs");
const { normalize } = require("../src/normalize.js");

const filename = process.argv[2];
const input = filename
  ? readFileSync(filename, "utf8")
  : readFileSync(0, "utf8");

process.stdout.write(normalize(input));