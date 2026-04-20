import AppBar from '@mui/material/AppBar';
import Box from '@mui/material/Box';
import Button from '@mui/material/Button';
import Container from '@mui/material/Container';
import Toolbar from '@mui/material/Toolbar';
import Typography from '@mui/material/Typography';

function PlaceholderPage() {
  return (
    <Box sx={{ flexGrow: 1 }}>
      <AppBar position="static">
        <Toolbar>
          <Typography variant="h6" component="div" sx={{ flexGrow: 1 }}>
            UPACIP — Unified Patient Access &amp; Clinical Intelligence Platform
          </Typography>
        </Toolbar>
      </AppBar>

      <Container maxWidth="md" sx={{ mt: 8, textAlign: 'center' }}>
        <Typography variant="h3" component="h1" gutterBottom color="primary">
          Welcome to UPACIP
        </Typography>
        <Typography variant="body1" color="text.secondary" sx={{ mb: 4 }}>
          The frontend scaffold is ready. Feature development begins here.
        </Typography>
        <Button variant="contained" color="primary" size="large">
          Get Started
        </Button>
      </Container>
    </Box>
  );
}

export default PlaceholderPage;
