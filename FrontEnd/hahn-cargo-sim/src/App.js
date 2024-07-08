import React, { useState } from 'react';
import Login from './Pages/Login';
import Dashboard from './Pages/Dashboard';
import { CssBaseline, Container } from '@mui/material';

const App = () => {
  const [token, setToken] = useState(null);

  return (
    <Container>
      <CssBaseline />
      {!token ? <Login setToken={setToken} /> : <Dashboard token={token} />}
    </Container>
  );
};

export default App;
