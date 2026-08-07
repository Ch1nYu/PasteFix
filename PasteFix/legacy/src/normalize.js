function joinContinuations(lines) {
  const result = [];
  let current = "";

  for (const line of lines) {
    const continues = /\\\s*$/.test(line);
    const segment = line.replace(/\\\s*$/, "").trim();

    current = current ? `${current} ${segment}` : segment;

    if (!continues) {
      result.push(current);
      current = "";
    }
  }

  if (current) {
    result.push(current);
  }

  return result;
}

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

  const normalizedLines = lines
    .map((line) => line.slice(commonIndent))
    .map((line) => line.replace(/^[>$]\s+/, ""));

  return joinContinuations(normalizedLines).join("\n");
}

module.exports = { normalize };