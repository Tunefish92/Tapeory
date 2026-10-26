import { Fragment, type ReactNode } from "react";

type Block =
  | { kind: "heading"; text: string }
  | { kind: "paragraph"; text: string }
  | { kind: "list"; items: ListItem[] };

interface ListItem {
  text: string;
  /** Indented "- " lines below the item. */
  children: string[];
}

/**
 * Reads the small part of Markdown that CHANGELOG.md uses: "###" headings, "- " bullets (with
 * indented continuation lines and one level of indented bullets) and paragraphs.
 */
export function parseReleaseNotes(markdown: string): Block[] {
  const blocks: Block[] = [];
  let paragraph: string[] = [];

  const endParagraph = () => {
    if (paragraph.length > 0) blocks.push({ kind: "paragraph", text: paragraph.join(" ") });
    paragraph = [];
  };

  for (const raw of markdown.split("\n")) {
    const line = raw.trimEnd();
    const last = blocks[blocks.length - 1];

    if (line.trim() === "") {
      endParagraph();
    } else if (line.startsWith("#")) {
      endParagraph();
      blocks.push({ kind: "heading", text: line.replace(/^#+\s*/, "") });
    } else if (/^[-*] /.test(line)) {
      endParagraph();
      const item: ListItem = { text: line.slice(2).trim(), children: [] };
      if (last?.kind === "list") last.items.push(item);
      else blocks.push({ kind: "list", items: [item] });
    } else if (/^\s/.test(line) && paragraph.length === 0 && last?.kind === "list") {
      const item = last.items[last.items.length - 1];
      const nested = line.trim();
      if (/^[-*] /.test(nested)) item.children.push(nested.slice(2).trim());
      else if (item.children.length > 0) item.children[item.children.length - 1] += ` ${nested}`;
      else item.text += ` ${nested}`;
    } else {
      paragraph.push(line.trim());
    }
  }

  endParagraph();
  return blocks;
}

/** **bold**, `code` and [links](https://…); everything else stays plain text. */
function renderInline(text: string): ReactNode[] {
  const pattern = /\*\*(.+?)\*\*|`([^`]+)`|\[([^\]]+)\]\((https?:\/\/[^)\s]+)\)/g;
  const nodes: ReactNode[] = [];
  let index = 0;

  for (const match of text.matchAll(pattern)) {
    if (match.index > index) nodes.push(text.slice(index, match.index));
    const key = match.index;
    if (match[1] !== undefined) nodes.push(<strong key={key}>{renderInline(match[1])}</strong>);
    else if (match[2] !== undefined) nodes.push(<code key={key}>{match[2]}</code>);
    else nodes.push(<a key={key} href={match[4]} target="_blank" rel="noreferrer">{match[3]}</a>);
    index = match.index + match[0].length;
  }

  if (index < text.length) nodes.push(text.slice(index));
  return nodes;
}

export function ReleaseNotes({ markdown }: { markdown: string }) {
  return (
    <>
      {parseReleaseNotes(markdown).map((block, i) => (
        <Fragment key={i}>
          {block.kind === "heading" && <h4>{renderInline(block.text)}</h4>}
          {block.kind === "paragraph" && <p>{renderInline(block.text)}</p>}
          {block.kind === "list" && (
            <ul>
              {block.items.map((item, j) => (
                <li key={j}>
                  {renderInline(item.text)}
                  {item.children.length > 0 && (
                    <ul>
                      {item.children.map((child, k) => (
                        <li key={k}>{renderInline(child)}</li>
                      ))}
                    </ul>
                  )}
                </li>
              ))}
            </ul>
          )}
        </Fragment>
      ))}
    </>
  );
}
