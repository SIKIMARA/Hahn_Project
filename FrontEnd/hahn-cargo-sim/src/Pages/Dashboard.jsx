import React, { useState, useEffect } from 'react';
import axios from 'axios';
import { Container, Typography, Button, Box, List, ListItem, ListItemText } from '@mui/material';

const Dashboard = ({ token }) => {
  const [coinAmount, setCoinAmount] = useState(0);
  const [transporters, setTransporters] = useState([]);
  const [orders, setOrders] = useState([]);

  useEffect(() => {
    const fetchCoinAmount = async () => {
      const response = await axios.get('https://localhost:7115/User/CoinAmount', {
        headers: { Authorization: token },
      });
      setCoinAmount(response.data);
    };

    const fetchTransporters = async () => {
      const response = await axios.get('https://localhost:7115/CargoTransporter/GetAll', {
        headers: { Authorization: token },
      });
      setTransporters(response.data);
    };

    const fetchOrders = async () => {
      const response = await axios.get('https://localhost:7115/Order/GetAllAvailable', {
        headers: { Authorization: token },
      });
      setOrders(response.data);
    };

    fetchCoinAmount();
    fetchTransporters();
    fetchOrders();
  }, [token]);

  const startSimulation = async () => {
    await axios.post('https://localhost:7115/Sim/Start', null, {
      headers: { Authorization: token },
    });
  };

  const stopSimulation = async () => {
    await axios.post('https://localhost:7115/Sim/Stop', null, {
      headers: { Authorization: token },
    });
  };

  return (
    <Container>
      <Box mt={5}>
        <Typography variant="h4" gutterBottom>Dashboard</Typography>
        <Typography variant="h6">Coin Amount: {coinAmount}</Typography>
        <Box mt={2}>
          <Button variant="contained" color="primary" onClick={startSimulation} sx={{ mr: 2 }}>Start Simulation</Button>
          <Button variant="contained" color="secondary" onClick={stopSimulation}>Stop Simulation</Button>
        </Box>
        <Box mt={4}>
          <Typography variant="h5">Transporters</Typography>
          <List>
            {transporters.map((transporter) => (
              <ListItem key={transporter.id}>
                <ListItemText primary={`ID: ${transporter.id}, Load: ${transporter.load}`} />
              </ListItem>
            ))}
          </List>
        </Box>
        <Box mt={4}>
          <Typography variant="h5">Orders</Typography>
          <List>
            {orders.map((order) => (
              <ListItem key={order.id}>
                <ListItemText primary={`Order ID: ${order.id}, Value: ${order.value}`} />
              </ListItem>
            ))}
          </List>
        </Box>
      </Box>
    </Container>
  );
};

export default Dashboard;
