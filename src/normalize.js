function normalize(input) {
  let lines = input.replace(/\r\n/g, "\n").split("\n");

  while (lines[0]?.trim() === "") lines.shift();
  while (lines.at(-1)?.trim() === "") lines.pop();

  if (/^```[a-zA-Z0-9_-]*\s*$/.test(lines[0] ?? "")) {
    lines.shift();

    if (/^```\s*$/.test(lines.at(-1) ?? "")) {
      lines.pop();
    }
  }

  const nonEmptyLines = lines.filter((line) => line.trim() !== "");

  if (nonEmptyLines.length === 0) {
    return "";
  }

  const commonIndent = Math.min(
    ...nonEmptyLines.map((line) => line.match(/^[\t ]*/)[0].length),
  );

  return lines
  .map((line) => line.slice(commonIndent))
  .map((line) => line.replace(/^[>$]\s+/, ""))
  .join("\n");
}

module.exports = { normalize };