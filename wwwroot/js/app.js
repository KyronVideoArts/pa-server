// Toast notification system
function showToast(msg, type = 'success') {
  const container = document.getElementById('toast-container') || (() => {
    const c = document.createElement('div');
    c.id = 'toast-container';
    document.body.appendChild(c);
    return c;
  })();
  const t = document.createElement('div');
  t.className = `toast ${type}`;
  t.textContent = msg;
  container.appendChild(t);
  setTimeout(() => t.remove(), 3200);
}

// Weapon icons lookup
const WEAPON_ICONS = {
  blaster:    '⚡',
  machinegun: '🔫',
  shotgun:    '💥',
  nailgun:    '🔩',
  lightning:  '🌩️',
};

// Get weapon icon
function getWeaponIcon(id) {
  return WEAPON_ICONS[id] || '🗡️';
}

// Compute stat bar values
function dpsScore(w) {
  const dps = (w.damage * (w.projectilesPerShot || 1)) / (w.fireIntervalSeconds || 1);
  return Math.min(100, (dps / 25) * 100);
}
function rangeScore(w) { return Math.min(100, (w.range / 6000) * 100); }
function speedScore(w) { return Math.max(5, 100 - (w.fireIntervalSeconds / 0.9) * 100); }

// Render a weapon card
function renderWeaponCard(weapon, owned, equipped, mode) {
  const icon = getWeaponIcon(weapon.id);
  const trigger = weapon.triggerMode.replace('_', ' ');
  const sim = weapon.simulationType;

  const isEquipped = equipped === weapon.id;
  const cls = ['weapon-card', owned ? '' : 'locked', isEquipped ? 'selected' : ''].filter(Boolean).join(' ');

  return `
    <div class="${cls}" data-id="${weapon.id}" data-owned="${owned}" onclick="handleWeaponClick('${weapon.id}', ${owned}, '${mode}')">
      <div class="weapon-icon">${icon}</div>
      <div class="weapon-name">${weapon.displayName}</div>
      <div class="weapon-type">${trigger} · ${sim}</div>
      <div class="weapon-stats">
        <div class="stat-row"><span class="stat-label">Damage</span><div class="stat-bar-wrap"><div class="stat-bar" style="width:${dpsScore(weapon).toFixed(0)}%"></div></div></div>
        <div class="stat-row"><span class="stat-label">Range</span><div class="stat-bar-wrap"><div class="stat-bar" style="width:${rangeScore(weapon).toFixed(0)}%"></div></div></div>
        <div class="stat-row"><span class="stat-label">Fire Rate</span><div class="stat-bar-wrap"><div class="stat-bar" style="width:${speedScore(weapon).toFixed(0)}%"></div></div></div>
      </div>
      <div class="weapon-price">
        ${owned 
          ? (isEquipped ? '<span class="equip-badge">✓ Equipped</span>' : '<span class="owned-tag">✓ Owned</span>')
          : '<span class="locked-tag">🔒 Locked</span>'}
        ${owned 
          ? (isEquipped ? '' : `<button class="btn-join" style="padding:0.3rem 0.75rem;font-size:0.75rem" onclick="event.stopPropagation();equipWeapon('${weapon.id}','${mode}')">Equip</button>`)
          : `<button class="btn-join" style="padding:0.3rem 0.75rem;font-size:0.75rem;background:linear-gradient(135deg,#a855f7,#7c3aed)" onclick="event.stopPropagation();buyWeapon('${weapon.id}','${mode}')">Buy</button>`}
      </div>
    </div>`;
}

// Buy weapon
async function buyWeapon(id, mode) {
  const isMp = mode === 'mp';
  const r = await fetch('/api/v1/store/buy', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ WeaponId: id, IsMultiplayer: isMp, Cost: 100 })
  });
  const txt = await r.text();
  showToast(txt, r.ok ? 'success' : 'error');
  if (r.ok) setTimeout(() => window.location.reload(), 800);
}

// Equip weapon
async function equipWeapon(id, mode) {
  const isMp = mode === 'mp';
  const r = await fetch('/api/v1/store/equip', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ WeaponId: id, IsMultiplayer: isMp })
  });
  const txt = await r.text();
  showToast(txt, r.ok ? 'success' : 'error');
  if (r.ok) setTimeout(() => window.location.reload(), 800);
}

// Handle weapon card click
function handleWeaponClick(id, owned, mode) {
  if (!owned) return;
  equipWeapon(id, mode);
}

// Equip special (gadget or perk)
async function equipSpecial(id, type, isMp) {
  const r = await fetch('/api/v1/store/equip_special', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ Id: id, Type: type, IsMultiplayer: isMp })
  });
  const txt = await r.text();
  showToast(txt, r.ok ? 'success' : 'error');
  if (r.ok) setTimeout(() => window.location.reload(), 800);
}

// Switch mode tabs
function switchMode(mode) {
  document.querySelectorAll('.tab-btn').forEach(b => {
    b.classList.remove('active-mp', 'active-sp');
    if (b.dataset.mode === mode) b.classList.add(mode === 'mp' ? 'active-mp' : 'active-sp');
  });

  document.querySelectorAll('.mode-panel').forEach(p => {
    p.style.display = p.dataset.mode === mode ? '' : 'none';
  });

  // Update save button style
  const saveBtn = document.getElementById('save-btn');
  if (saveBtn) {
    saveBtn.className = 'btn-save ' + (mode === 'sp' ? 'sp-mode' : '');
    saveBtn.dataset.mode = mode;
  }

  window._currentMode = mode;
}

// Init page
document.addEventListener('DOMContentLoaded', () => {
  switchMode('mp');

  // Auto-refresh server list
  if (document.getElementById('server-list')) {
    setInterval(refreshServers, 20000);
  }
});

async function refreshServers() {
  try {
    const r = await fetch('/api/v1/servers');
    if (!r.ok) return;
    const data = await r.json();
    const list = document.getElementById('server-list');
    if (!list) return;
    if (data.length === 0) {
      list.innerHTML = '<div class="empty-state"><p>No servers online right now.</p></div>';
      return;
    }
    list.innerHTML = data.map(s => `
      <a class="server-row" href="${s.joinUrl || '#'}">
        <div class="server-status-dot"></div>
        <div style="flex:1">
          <div class="server-name">${escHtml(s.name)}</div>
          <div class="server-map" style="display:flex;gap:1.25rem;margin-top:0.4rem;align-items:center">
            <span>📍 ${escHtml(s.map)}</span>
            <span style="color:var(--text-muted);font-size:0.75rem;padding:0.1rem 0.4rem;border-radius:4px;border:1px solid rgba(255,255,255,0.1)">${escHtml(s.mode)}</span>
            <span style="color:var(--text-muted);display:flex;align-items:center;gap:0.3rem">
              <svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M20 21v-2a4 4 0 0 0-4-4H8a4 4 0 0 0-4 4v2"></path><circle cx="12" cy="7" r="4"></circle></svg>
              ${escHtml(s.hostName)}
            </span>
            <span style="color:var(--text-muted);display:flex;align-items:center;gap:0.3rem">
              👥 ${s.players}/${s.maxPlayers}
            </span>
            <span style="color:var(--text-muted);background:rgba(255,255,255,0.05);padding:0.1rem 0.4rem;border-radius:4px;font-family:monospace;font-size:0.75rem" title="Connect via console: connect master_ip:${s.port}">
              🔌 ${s.port}
            </span>
            ${s.bots ? "<span title='Bots Enabled'>🤖</span>" : ""}
          </div>
        </div>
        <div style="display:flex;gap:0.4rem;align-items:center;height:fit-content">
          ${s.official ? '<span class="server-badge badge-official">Official</span>' : '<span class="server-badge badge-p2p">P2P</span>'}
          ${s.locked ? '<span class="server-badge badge-locked">🔒</span>' : ''}
        </div>
        <div style="display:flex;align-items:center">
          ${s.joinUrl ? '<span class="btn-join">Join →</span>' : '<span style="font-size:0.8rem;color:var(--text-muted)">Login to Join</span>'}
        </div>
      </a>`).join('');
  } catch(e) {}
}

function escHtml(str) {
  const d = document.createElement('div');
  d.textContent = str;
  return d.innerHTML;
}
