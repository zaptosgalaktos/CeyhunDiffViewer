#!/usr/bin/env bash
# Creates a throwaway git repo with a prefab edited across two commits,
# so you can exercise uadiff end-to-end:
#
#   ./samples/make-sample-repo.sh /tmp/uadiff-sample
#   dotnet run --project src/CeyhunDiffViewer.Cli -- scan /tmp/uadiff-sample HEAD~1 HEAD
#   dotnet run --project src/CeyhunDiffViewer.Cli -- diff /tmp/uadiff-sample HEAD~1 HEAD \
#       --path Assets/Card.prefab
set -euo pipefail

DEST="${1:-/tmp/uadiff-sample}"
rm -rf "$DEST"
mkdir -p "$DEST/Assets"
cd "$DEST"
git init -q
git config user.email sample@example.com
git config user.name Sample

# --- .meta files give each asset its stable guid identity ---
cat > Assets/Card.prefab.meta <<'EOF'
fileFormatVersion: 2
guid: c910a4271b4de414288a0eca230dbc57
EOF

# --- v1 of the prefab ---
cat > Assets/Card.prefab <<'EOF'
%YAML 1.1
%TAG !u! tag:unity3d.com,2011:
--- !u!1 &100
GameObject:
  m_Name: Card
  m_Component:
  - component: {fileID: 400}
--- !u!4 &400
Transform:
  m_GameObject: {fileID: 100}
  m_Father: {fileID: 0}
--- !u!114 &500
MonoBehaviour:
  m_GameObject: {fileID: 100}
  m_Sprite: {fileID: 21300000, guid: 495f427b4262d4d5ba15ba370938d12b, type: 3}
EOF

git add -A
git commit -qm "v1: initial Card prefab"

# --- v2: swap the sprite guid, no structural change ---
cat > Assets/Card.prefab <<'EOF'
%YAML 1.1
%TAG !u! tag:unity3d.com,2011:
--- !u!1 &100
GameObject:
  m_Name: Card
  m_Component:
  - component: {fileID: 400}
--- !u!4 &400
Transform:
  m_GameObject: {fileID: 100}
  m_Father: {fileID: 0}
--- !u!114 &500
MonoBehaviour:
  m_GameObject: {fileID: 100}
  m_Sprite: {fileID: 21300000, guid: 8bae82d1549c24b22b6fdd9b07bc5384, type: 3}
EOF

git add -A
git commit -qm "v2: swap Card sprite"

echo "Sample repo ready at: $DEST"
echo "Latest two commits:"
git --no-pager log --oneline -2
