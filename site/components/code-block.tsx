'use client'

import { useState, useCallback } from 'react'

interface CodeBlockProps {
  code: string
  language?: string
}

/**
 * Code block with a copy-to-clipboard button.
 *
 * The button appears on hover and copies the raw code text. Visual feedback
 * ("Copied") confirms the action. Used on the templates page for each template's
 * usage example and the general "Using one" snippet.
 */
export function CodeBlock({ code, language = 'csharp' }: CodeBlockProps) {
  const [copied, setCopied] = useState(false)

  const copy = useCallback(async () => {
    try {
      await navigator.clipboard.writeText(code)
      setCopied(true)
      setTimeout(() => setCopied(false), 2000)
    } catch {
      // Clipboard API may be unavailable (permissions, non-secure context).
      // Fall back to a temporary textarea + execCommand.
      const textarea = document.createElement('textarea')
      textarea.value = code
      textarea.style.position = 'fixed'
      textarea.style.opacity = '0'
      document.body.appendChild(textarea)
      textarea.select()
      try {
        document.execCommand('copy')
        setCopied(true)
        setTimeout(() => setCopied(false), 2000)
      } catch {
        // Silently fail — the user can still select and copy manually.
      }
      document.body.removeChild(textarea)
    }
  }, [code])

  return (
    <div className="code-block-wrapper" style={{ position: 'relative' }}>
      <pre data-language={language}>
        <code>{code}</code>
      </pre>
      <button
        type="button"
        className={`code-copy-btn ${copied ? 'copied' : ''}`}
        onClick={copy}
        aria-label={copied ? 'Copied' : 'Copy code'}
      >
        {copied ? (
          <>
            <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" width="14" height="14">
              <polyline points="20 6 9 17 4 12" />
            </svg>
            Copied
          </>
        ) : (
          <>
            <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" width="14" height="14">
              <rect x="9" y="9" width="13" height="13" rx="2" ry="2" />
              <path d="M5 15H4a2 2 0 0 1-2-2V4a2 2 0 0 1 2-2h9a2 2 0 0 1 2 2v1" />
            </svg>
            Copy
          </>
        )}
      </button>
    </div>
  )
}
