import { useRef, type KeyboardEvent } from 'react';
import List from '@mui/material/List';
import ListItem from '@mui/material/ListItem';
import ListItemButton from '@mui/material/ListItemButton';
import ListItemIcon from '@mui/material/ListItemIcon';
import ListItemText from '@mui/material/ListItemText';
import Drawer from '@mui/material/Drawer';
import Divider from '@mui/material/Divider';
import DashboardIcon from '@mui/icons-material/Dashboard';
import CalendarMonthIcon from '@mui/icons-material/CalendarMonth';
import HistoryIcon from '@mui/icons-material/History';
import PersonIcon from '@mui/icons-material/Person';
import UploadFileIcon from '@mui/icons-material/UploadFile';
import { useLocation, useNavigate } from 'react-router-dom';

const DRAWER_WIDTH = 240;

interface NavItem {
  label: string;
  path: string;
  icon: React.ReactNode;
}

const NAV_ITEMS: NavItem[] = [
  { label: 'Dashboard',            path: '/dashboard',            icon: <DashboardIcon /> },
  { label: 'Book appointment',     path: '/appointments/book',    icon: <CalendarMonthIcon /> },
  { label: 'Appointment history',  path: '/appointments/history', icon: <HistoryIcon /> },
  { label: 'My profile',           path: '/profile',              icon: <PersonIcon /> },
  { label: 'Upload documents',     path: '/documents/upload',     icon: <UploadFileIcon /> },
];

interface SidebarProps {
  open?: boolean;
  onClose?: () => void;
}

/**
 * Accessible sidebar navigation for patient-facing screens (US_100, AC-2, AC-3).
 *
 * Keyboard behavior (WAI-ARIA Menu pattern):
 * - Tab enters the menu at the currently active item (roving tabindex).
 * - ArrowDown / ArrowUp cycles focus through items.
 * - Home / End jumps to first / last item.
 * - Enter activates the focused item.
 *
 * ARIA attributes:
 * - `role="navigation"` with `aria-label="Main navigation"` — landmark landmark region.
 * - `role="menuitem"` on each list item — communicates interactive list semantics.
 * - `aria-current="page"` on the active item — announced by screen readers.
 * - `aria-hidden="true"` on decorative icons — prevents redundant announcements.
 */
export function Sidebar({ open = true, onClose }: SidebarProps) {
  const location = useLocation();
  const navigate = useNavigate();
  const listRef = useRef<HTMLUListElement>(null);

  function handleKeyDown(e: KeyboardEvent<HTMLElement>, index: number) {
    if (!listRef.current) return;
    const items = Array.from(
      listRef.current.querySelectorAll<HTMLElement>('[role="menuitem"]'),
    );
    let targetIndex = index;

    switch (e.key) {
      case 'ArrowDown':
        e.preventDefault();
        targetIndex = (index + 1) % items.length;
        break;
      case 'ArrowUp':
        e.preventDefault();
        targetIndex = (index - 1 + items.length) % items.length;
        break;
      case 'Home':
        e.preventDefault();
        targetIndex = 0;
        break;
      case 'End':
        e.preventDefault();
        targetIndex = items.length - 1;
        break;
      default:
        return;
    }

    items[targetIndex].focus();
  }

  const drawerContent = (
    <nav aria-label="Main navigation">
      <Divider />
      <List
        ref={listRef}
        role="menu"
        aria-label="Navigation menu"
        sx={{ pt: 0 }}
      >
        {NAV_ITEMS.map((item, index) => {
          const isActive = location.pathname === item.path;
          return (
            <ListItem key={item.path} disablePadding>
              <ListItemButton
                role="menuitem"
                tabIndex={isActive ? 0 : -1}
                aria-current={isActive ? 'page' : undefined}
                selected={isActive}
                onKeyDown={(e) => handleKeyDown(e, index)}
                onClick={() => {
                  navigate(item.path);
                  onClose?.();
                }}
                sx={{
                  '&.Mui-selected': {
                    backgroundColor: 'action.selected',
                    '&:hover': { backgroundColor: 'action.selected' },
                  },
                }}
              >
                {/* aria-hidden: decorative icon — the label provides the accessible name */}
                <ListItemIcon aria-hidden="true" sx={{ minWidth: 40 }}>
                  {item.icon}
                </ListItemIcon>
                <ListItemText primary={item.label} />
              </ListItemButton>
            </ListItem>
          );
        })}
      </List>
    </nav>
  );

  return (
    <Drawer
      variant={onClose ? 'temporary' : 'permanent'}
      open={open}
      onClose={onClose}
      ModalProps={{ keepMounted: true }}
      sx={{
        width: DRAWER_WIDTH,
        flexShrink: 0,
        '& .MuiDrawer-paper': {
          width: DRAWER_WIDTH,
          boxSizing: 'border-box',
        },
      }}
    >
      {drawerContent}
    </Drawer>
  );
}
