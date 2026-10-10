param([string]$Root, [int]$Port = 8899)

# Minimal static server used to eyeball the GitHub Pages export locally.
#
# It mounts the export at /Delibera on purpose: the deployed site lives under that prefix, and
# verifying a build at "/" would hide every missing-basePath bug. Directory requests resolve to
# index.html, which is what Pages does; anything unmatched returns the exported 404.html.

$listener = New-Object System.Net.HttpListener
$listener.Prefixes.Add("http://localhost:$Port/")
$listener.Start()
Write-Output "serving $Root at http://localhost:$Port/"

while ($listener.IsListening) {
  try {
    $ctx = $listener.GetContext()
  } catch {
    break
  }

  $rel = $ctx.Request.Url.AbsolutePath.TrimStart('/') -replace '/', '\'
  $path = Join-Path $Root $rel

  if (Test-Path $path -PathType Container) {
    $path = Join-Path $path 'index.html'
  }

  if (Test-Path $path -PathType Leaf) {
    $status = 200
  } else {
    $path = Join-Path $Root '404.html'
    $status = 404
  }

  try {
    $bytes = [System.IO.File]::ReadAllBytes($path)
    $ctx.Response.StatusCode = $status
    $ctx.Response.ContentType =
      if ($path.EndsWith('.html')) { 'text/html; charset=utf-8' }
      elseif ($path.EndsWith('.css')) { 'text/css; charset=utf-8' }
      elseif ($path.EndsWith('.js')) { 'text/javascript; charset=utf-8' }
      elseif ($path.EndsWith('.png')) { 'image/png' }
      elseif ($path.EndsWith('.svg')) { 'image/svg+xml' }
      else { 'application/octet-stream' }
    $ctx.Response.ContentLength64 = $bytes.Length
    $ctx.Response.OutputStream.Write($bytes, 0, $bytes.Length)
    $ctx.Response.OutputStream.Close()
  } catch {
    # Client disconnected mid-write; nothing useful to do.
  }
}