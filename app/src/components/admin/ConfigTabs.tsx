/**
 * ConfigTabs — Tabbed configuration container for SCR-015 (US_058 AC-4).
 *
 * Tabs: "Slot Templates" | "Notifications" | "Hours & Holidays" | "Risk Thresholds"
 * Mobile (xs): variant="scrollable" (UXR-303).
 * Accessibility: role="tablist", aria-controls, aria-selected on each Tab.
 */

import Box from '@mui/material/Box';
import Paper from '@mui/material/Paper';
import Tab from '@mui/material/Tab';
import Tabs from '@mui/material/Tabs';
import useMediaQuery from '@mui/material/useMediaQuery';
import { useTheme } from '@mui/material/styles';
import { useState } from 'react';
import BusinessHoursPanel from './BusinessHoursPanel';
import NotificationTemplatesPanel from './NotificationTemplatesPanel';
import RiskThresholdsPanel from './RiskThresholdsPanel';
import SlotTemplatesPanel from './SlotTemplatesPanel';

// ─── Tab panel helper ─────────────────────────────────────────────────────────

interface TabPanelProps {
  index: number;
  value: number;
  children: React.ReactNode;
}

function TabPanel({ index, value, children }: TabPanelProps) {
  return (
    <Box
      role="tabpanel"
      hidden={value !== index}
      id={`config-tabpanel-${index}`}
      aria-labelledby={`config-tab-${index}`}
      sx={{ pt: 3 }}
    >
      {value === index && children}
    </Box>
  );
}

// ─── Tab config ───────────────────────────────────────────────────────────────

const TABS = [
  { label: 'Slot Templates',     panel: <SlotTemplatesPanel /> },
  { label: 'Notifications',      panel: <NotificationTemplatesPanel /> },
  { label: 'Hours & Holidays',   panel: <BusinessHoursPanel /> },
  { label: 'Risk Thresholds',    panel: <RiskThresholdsPanel /> },
];

// ─── Component ────────────────────────────────────────────────────────────────

export default function ConfigTabs() {
  const [activeTab, setActiveTab] = useState(0);
  const theme    = useTheme();
  const isMobile = useMediaQuery(theme.breakpoints.down('sm'));

  return (
    <Paper variant="outlined" sx={{ borderRadius: 2, overflow: 'hidden' }}>
      {/* Tab bar */}
      <Box sx={{ borderBottom: 1, borderColor: 'divider' }}>
        <Tabs
          value={activeTab}
          onChange={(_, v: number) => setActiveTab(v)}
          variant={isMobile ? 'scrollable' : 'standard'}
          scrollButtons={isMobile ? 'auto' : false}
          aria-label="Configuration tabs"
          role="tablist"
          textColor="inherit"
          TabIndicatorProps={{ style: { backgroundColor: 'var(--mui-palette-error-main, #d32f2f)' } }}
          sx={{ px: 2, pt: 1 }}
        >
          {TABS.map((t, i) => (
            <Tab
              key={t.label}
              label={t.label}
              id={`config-tab-${i}`}
              aria-controls={`config-tabpanel-${i}`}
              aria-selected={activeTab === i}
            />
          ))}
        </Tabs>
      </Box>

      {/* Tab panels */}
      <Box sx={{ p: { xs: 2, sm: 3 } }}>
        {TABS.map((t, i) => (
          <TabPanel key={t.label} index={i} value={activeTab}>
            {t.panel}
          </TabPanel>
        ))}
      </Box>
    </Paper>
  );
}
