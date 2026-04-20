import Box from '@mui/material/Box';
import Button from '@mui/material/Button';
import Container from '@mui/material/Container';
import Typography from '@mui/material/Typography';
import { Link } from 'react-router-dom';

function NotFoundPage() {
  return (
    <Container maxWidth="sm" sx={{ mt: 12, textAlign: 'center' }}>
      <Typography variant="h1" component="h1" color="primary" gutterBottom>
        404
      </Typography>
      <Typography variant="h5" gutterBottom>
        Page Not Found
      </Typography>
      <Typography variant="body1" color="text.secondary" sx={{ mb: 4 }}>
        The page you are looking for does not exist or has been moved.
      </Typography>
      <Box>
        <Button component={Link} to="/" variant="contained" color="primary">
          Return to Home
        </Button>
      </Box>
    </Container>
  );
}

export default NotFoundPage;
